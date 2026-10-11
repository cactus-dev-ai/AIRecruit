using System.Data;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RecruitAI.Application.Common;
using RecruitAI.Application.Imports;
using RecruitAI.Infrastructure.Database;

namespace RecruitAI.Infrastructure.Imports;

public sealed class SqlResumeImportService(
    SqlConnectionFactory connectionFactory,
    ResumeImportSettings settings) : IResumeImportService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".json" };
    private static readonly SemaphoreSlim ScanLock = new(1, 1);

    public async Task<ResumeScanResult> ScanAsync(long userId, CancellationToken cancellationToken)
    {
        await ScanLock.WaitAsync(cancellationToken);
        try { return await ScanCoreAsync(userId, cancellationToken); }
        finally { ScanLock.Release(); }
    }

    public async Task<ManualResumeUploadResult> UploadManualAsync(
        IReadOnlyList<ManualResumeUploadFile> files, long jobId, long userId, CancellationToken cancellationToken)
    {
        if (jobId <= 0) throw new BusinessException("INVALID_JOB_ID", "请选择有效岗位。");
        if (files.Count == 0) throw new BusinessException("MANUAL_RESUME_REQUIRED", "请选择要上传的简历。");
        if (files.Count > settings.ManualMaxFilesPerUpload)
            throw new BusinessException("TOO_MANY_MANUAL_RESUMES", $"每次最多上传 {settings.ManualMaxFilesPerUpload} 份简历。");

        await using (var checkConnection = await connectionFactory.OpenAsync(cancellationToken))
        await using (var check = new SqlCommand("SELECT COUNT(*) FROM dbo.Job WHERE Id=@Id AND IsDeleted=0 AND Status='OPEN'", checkConnection))
        {
            check.Parameters.AddWithValue("@Id", jobId);
            if (Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) != 1)
                throw new BusinessException("JOB_NOT_OPEN", "所选岗位不存在或未开放。", 409);
        }

        var batchKey = Guid.NewGuid();
        var saved = new List<ManualSavedFile>(files.Count);
        var createdPaths = new List<string>(files.Count);
        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var originalName = Path.GetFileName(file.FileName?.Trim());
                if (string.IsNullOrWhiteSpace(originalName) || originalName.Length > 260)
                    throw new BusinessException("INVALID_MANUAL_FILE_NAME", "简历文件名无效或超过260个字符。");
                var extension = Path.GetExtension(originalName).ToLowerInvariant();
                if (extension is not ".pdf" and not ".docx")
                    throw new BusinessException("UNSUPPORTED_MANUAL_FILE_TYPE", $"{originalName} 不是支持的 PDF 或 DOCX 文件。");
                if (file.Length <= 0 || file.Length > settings.ManualMaxFileBytes)
                    throw new BusinessException("MANUAL_FILE_SIZE_INVALID", $"{originalName} 为空或超过 {settings.ManualMaxFileBytes / 1024 / 1024}MB。");

                var relative = Path.Combine("Manual", batchKey.ToString("N"), $"{Guid.NewGuid():N}{extension}");
                var fullPath = ResolveRelative(relative);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await using (var target = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    await file.Content.CopyToAsync(target, cancellationToken);
                createdPaths.Add(fullPath);
                var actualLength = new FileInfo(fullPath).Length;
                if (actualLength != file.Length || !HasValidManualStructure(fullPath, extension))
                    throw new BusinessException("MANUAL_FILE_STRUCTURE_INVALID", $"{originalName} 文件不完整、损坏或扩展名与内容不符。");
                await using var hashStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, cancellationToken));
                if (saved.Any(x => x.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase)) || await FormalResumeHashExistsAsync(hash, cancellationToken))
                    throw new BusinessException("DUPLICATE_RESUME", $"{originalName} 与本次其他文件或正式简历库中的文件重复。", 409);
                saved.Add(new(originalName, extension.TrimStart('.').ToUpperInvariant(), relative, actualLength, hash, fullPath));
            }

            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                await using (var batch = new SqlCommand("""
                    INSERT dbo.ResumeImportBatch(BatchKey,Status,CreatedBy,ConfirmedBy,ConfirmTime,SourceFileCount,HrUploadCount,ThirdPartyCount)
                    VALUES(@Key,'PROCESSING',@UserId,@UserId,SYSUTCDATETIME(),@Count,@Count,0);
                    SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
                    """, connection, transaction))
                {
                    batch.Parameters.AddWithValue("@Key", batchKey); batch.Parameters.AddWithValue("@UserId", userId); batch.Parameters.AddWithValue("@Count", saved.Count);
                    var batchId = Convert.ToInt64(await batch.ExecuteScalarAsync(cancellationToken));
                    await using (var job = new SqlCommand("INSERT dbo.ResumeImportBatchJob(BatchId,JobId) VALUES(@BatchId,@JobId)", connection, transaction))
                    {
                        job.Parameters.AddWithValue("@BatchId", batchId); job.Parameters.AddWithValue("@JobId", jobId);
                        await job.ExecuteNonQueryAsync(cancellationToken);
                    }
                    foreach (var item in saved)
                    {
                        await using var insert = new SqlCommand("""
                            INSERT dbo.ResumeImportFile(BatchId,SourceType,OriginalFileName,FileExtension,OriginalSourcePath,TempFilePath,FileSize,FileHash,Status,ReviewedBy,ReviewTime)
                            VALUES(@BatchId,'HR_UPLOAD',@Name,@Extension,@OriginalPath,@TempPath,@Size,@Hash,'QUEUED',@UserId,SYSUTCDATETIME());
                            DECLARE @FileId BIGINT; SET @FileId=SCOPE_IDENTITY();
                            INSERT dbo.AITask(TaskType,ResumeImportFileId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey,NextRunTime,CreateTime,UpdateTime)
                            VALUES('MANUAL_RESUME_IMPORT',@FileId,'WAITING',6,0,0,'manual-resume-import:'+CONVERT(VARCHAR(30),@FileId)+':'+@Hash,SYSUTCDATETIME(),SYSUTCDATETIME(),SYSUTCDATETIME());
                            """, connection, transaction);
                        insert.Parameters.AddWithValue("@BatchId", batchId); insert.Parameters.AddWithValue("@UserId", userId);
                        insert.Parameters.Add("@Name", SqlDbType.NVarChar, 260).Value = item.OriginalName;
                        insert.Parameters.AddWithValue("@Extension", item.Extension);
                        insert.Parameters.Add("@OriginalPath", SqlDbType.NVarChar, 1000).Value = Path.Combine("Manual", item.OriginalName);
                        insert.Parameters.Add("@TempPath", SqlDbType.NVarChar, 1000).Value = item.RelativePath;
                        insert.Parameters.AddWithValue("@Size", item.Length); insert.Parameters.AddWithValue("@Hash", item.Hash);
                        await insert.ExecuteNonQueryAsync(cancellationToken);
                    }
                    await transaction.CommitAsync(cancellationToken);
                    return new(batchId, saved.Count, "PROCESSING", $"已保存 {saved.Count} 份简历，人工上传 Agent 正在解析入库。");
                }
            }
            catch { await transaction.RollbackAsync(cancellationToken); throw; }
        }
        catch
        {
            foreach (var path in createdPaths) try { File.Delete(path); } catch (IOException) { }
            throw;
        }
    }

    private async Task<ResumeScanResult> ScanCoreAsync(long userId, CancellationToken cancellationToken)
    {
        await EnsureNoPendingReviewAsync(userId, cancellationToken);
        Directory.CreateDirectory(settings.ResumeTempRoot);
        var filteredCandidates = await RemoveAlreadyImportedThirdPartyJsonAsync(GetCandidates().ToList(), cancellationToken);
        var removedAlreadyImported = filteredCandidates.RemovedCount;
        var availableCandidates = filteredCandidates.Items;
        if (availableCandidates.Count == 0)
            return new("EMPTY", null, 0, 0, 0, 0, 0, false, removedAlreadyImported > 0 ? $"已清理{removedAlreadyImported}份已入库的51JOB简历，无可用简历。" : "无可用简历。");

        var hasMore = availableCandidates.Count > settings.MaxFilesPerScan;
        var candidates = availableCandidates.Take(settings.MaxFilesPerScan).ToList();
        var batchKey = Guid.NewGuid();
        var batchId = await CreateBatchAsync(batchKey, userId, cancellationToken);
        var reviewReady = 0;
        var failed = 0;
        var hrCount = 0;
        var thirdCount = 0;
        var batchHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            const string sourceType = "THIRD_PARTY_IMPORT";
            thirdCount++;
            var extension = Path.GetExtension(candidate.FullPath);
            var claimedRelative = Path.Combine("Processing", batchKey.ToString("N"), $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
            var claimedFullPath = ResolveRelative(claimedRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(claimedFullPath)!);

            try
            {
                File.Move(candidate.FullPath, claimedFullPath);
            }
            catch (IOException)
            {
                continue; // 被并发扫描者先认领，不计入本批。
            }

            var info = new FileInfo(claimedFullPath);
            var status = "REVIEW_READY";
            string? errorCode = null;
            string? errorMessage = null;
            long? sourceJobId = null;
            long? externalJobMappingId = null;
            if (info.Length <= 0)
            {
                status = "FAILED"; errorCode = "EMPTY_FILE"; errorMessage = "文件为空。";
            }
            else if (info.Length > settings.HardMaxSourceFileBytes)
            {
                status = "FAILED"; errorCode = "SOURCE_FILE_TOO_LARGE"; errorMessage = "源文件超过500MB硬安全上限。";
            }
            else if (!HasValidStructure(claimedFullPath, extension, candidate.OriginalName))
            {
                status = "FAILED"; errorCode = "FILE_STRUCTURE_INVALID"; errorMessage = "文件扩展名与内部结构不符或结构损坏。";
            }

            if (status == "REVIEW_READY")
            {
                var sourceJob = TryReadSourceJob(claimedFullPath);
                var mapping = sourceJob is null ? null : await FindExternalJobMappingAsync(sourceJob.Value, cancellationToken);
                if (mapping is null)
                {
                    status = "FAILED"; errorCode = "SOURCE_JOB_MAPPING_MISSING"; errorMessage = "JSON 未包含有效外部岗位信息，或该岗位尚未经过上传预检。";
                }
                else
                {
                    externalJobMappingId = mapping.Value.MappingId;
                    sourceJobId = mapping.Value.JobId;
                }
            }

            string hash;
            await using (var stream = new FileStream(claimedFullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));

            if (!batchHashes.Add(hash))
            {
                status = "FAILED"; errorCode = "DUPLICATE_IN_BATCH"; errorMessage = "本批次已认领相同文件。";
            }
            else if (status == "REVIEW_READY" && await FormalResumeHashExistsAsync(hash, cancellationToken))
            {
                status = "FAILED"; errorCode = "DUPLICATE_RESUME"; errorMessage = "正式简历库已存在相同文件。";
            }

            if (status == "FAILED")
            {
                claimedRelative = MoveToFailed(claimedFullPath, batchKey, extension);
                failed++;
            }
            else
            {
                reviewReady++;
            }

            var originalRelative = Path.Combine(candidate.SourceFolder, candidate.OriginalName);
            await InsertFileAsync(batchId, sourceType, candidate.OriginalName, extension.TrimStart('.').ToUpperInvariant(),
                originalRelative, claimedRelative, info.Length, hash, status, errorCode, errorMessage, externalJobMappingId, sourceJobId, cancellationToken);
        }

        var selected = reviewReady + failed;
        if (selected == 0)
        {
            await DeleteEmptyBatchAsync(batchId, cancellationToken);
            return new("EMPTY", null, 0, 0, 0, 0, 0, hasMore, "文件已被其他用户认领，请再次扫描。 ");
        }

        await UpdateBatchCountsAsync(batchId, selected, hrCount, thirdCount, failed, cancellationToken);
        if (reviewReady == 0)
        {
            await MarkBatchFailedAsync(batchId, cancellationToken);
            return new("BATCH_CREATED", batchId, selected, 0, failed, hrCount, thirdCount, hasMore,
                $"已读取{selected}个源文件，全部未通过文件校验，请在异常列表中处理后再次扫描。");
        }
        return new("BATCH_CREATED", batchId, selected, reviewReady, failed, hrCount, thirdCount, hasMore,
            $"已读取{selected}个源文件，请审阅后确认导入。已自动清理{removedAlreadyImported}份已入库的51JOB简历。");
    }

    public async Task<ResumeImportBatchPage> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        const string sql = """
            WITH Paged AS (
              SELECT b.Id,b.BatchKey,b.Status,b.Remark,b.SourceFileCount,b.SuccessCount,b.FailedCount,
                     u.DisplayName,b.CreateTime,b.RowVersion,
                     ROW_NUMBER() OVER (ORDER BY b.CreateTime DESC,b.Id DESC) AS RowNo
              FROM dbo.ResumeImportBatch b JOIN dbo.AppUser u ON u.Id=b.CreatedBy
            )
            SELECT Id,BatchKey,Status,Remark,SourceFileCount,SuccessCount,FailedCount,DisplayName,CreateTime,RowVersion
            FROM Paged WHERE RowNo BETWEEN @Start AND @End ORDER BY RowNo
            """;
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var totalCommand = new SqlCommand("SELECT COUNT(*) FROM dbo.ResumeImportBatch", connection);
        var totalCount = Convert.ToInt32(await totalCommand.ExecuteScalarAsync(cancellationToken));
        await totalCommand.DisposeAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Start", (page - 1) * pageSize + 1);
        command.Parameters.AddWithValue("@End", page * pageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ResumeImportBatchSummary>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(MapBatch(reader));
        return new(result, totalCount, page, pageSize);
    }

    public async Task<ResumeImportBatchDetail?> GetAsync(long batchId, CancellationToken cancellationToken)
    {
        const string batchSql = """
            SELECT b.Id,b.BatchKey,b.Status,b.Remark,b.SourceFileCount,b.SuccessCount,b.FailedCount,
                   u.DisplayName,b.CreateTime,b.RowVersion
            FROM dbo.ResumeImportBatch b JOIN dbo.AppUser u ON u.Id=b.CreatedBy WHERE b.Id=@Id
            """;
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        ResumeImportBatchSummary? batch;
        await using (var command = new SqlCommand(batchSql, connection))
        {
            command.Parameters.AddWithValue("@Id", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            batch = await reader.ReadAsync(cancellationToken) ? MapBatch(reader) : null;
        }
        if (batch is null) return null;

        const string filesSql = """
            SELECT Id,SourceType,OriginalFileName,FileExtension,FileSize,Status,ErrorCode,ErrorMessage,DetectedResumeCount,CreateTime
            FROM dbo.ResumeImportFile WHERE BatchId=@Id ORDER BY Id
            """;
        var files = new List<ResumeImportFileDto>();
        await using (var command = new SqlCommand(filesSql, connection))
        {
            command.Parameters.AddWithValue("@Id", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) files.Add(MapFile(reader));
        }
        var jobs = new List<long>();
        await using (var command = new SqlCommand("SELECT JobId FROM dbo.ResumeImportBatchJob WHERE BatchId=@Id ORDER BY JobId", connection))
        {
            command.Parameters.AddWithValue("@Id", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) jobs.Add(reader.GetInt64(0));
        }
        return new(batch, files, jobs);
    }

    public async Task RejectAsync(long batchId, long fileId, long userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var ownerId = await GetBatchOwnerAsync(batchId, cancellationToken);
        if (!isAdmin && ownerId != userId) throw new BusinessException("IMPORT_BATCH_FORBIDDEN", "只有批次创建人或管理员可以拒绝文件。", 403);
        var file = await GetFilePathAsync(fileId, batchId, "REVIEW_READY", cancellationToken)
            ?? throw new BusinessException("IMPORT_FILE_NOT_REVIEWABLE", "文件不存在或已不处于待审阅状态。", 409);
        var source = ResolveRelative(file.Path);
        var failedRelative = MoveToFailed(source, file.BatchKey, Path.GetExtension(file.Path));
        const string sql = """
            UPDATE dbo.ResumeImportFile SET Status='REJECTED',TempFilePath=@Path,ErrorCode='HR_REJECTED',
              ErrorMessage=N'HR审阅时拒绝导入。',ReviewedBy=@UserId,ReviewTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME()
            WHERE Id=@FileId AND BatchId=@BatchId AND Status='REVIEW_READY'
            """;
        await ExecuteFileUpdateAsync(sql, fileId, batchId, userId, failedRelative, cancellationToken);
    }

    public async Task<ConfirmResumeImportResult> ConfirmAsync(long batchId, ConfirmResumeImportRequest request, long userId, bool isAdmin, CancellationToken cancellationToken)
    {
        if (request.JobIds.Count != request.JobIds.Distinct().Count() || request.JobIds.Any(x => x <= 0))
            throw new BusinessException("INVALID_JOB_IDS", "岗位编号必须是不重复的正整数。 ");
        if (request.Remark?.Length > 500) throw new BusinessException("INVALID_REMARK", "备注不能超过500字。 ");
        byte[] rowVersion;
        try { rowVersion = Convert.FromBase64String(request.RowVersion); }
        catch { throw new BusinessException("INVALID_ROW_VERSION", "并发版本格式无效。 "); }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            const string ownerSql = "SELECT CreatedBy FROM dbo.ResumeImportBatch WITH (UPDLOCK,HOLDLOCK) WHERE Id=@Id AND Status='REVIEWING'";
            await using var ownerCommand = new SqlCommand(ownerSql, connection, transaction);
            ownerCommand.Parameters.AddWithValue("@Id", batchId);
            var owner = await ownerCommand.ExecuteScalarAsync(cancellationToken);
            if (owner is null) throw new BusinessException("IMPORT_BATCH_NOT_REVIEWING", "批次不存在或已确认。", 409);
            if (!isAdmin && Convert.ToInt64(owner) != userId) throw new BusinessException("IMPORT_BATCH_FORBIDDEN", "只有批次创建人或管理员可以确认。", 403);

            foreach (var jobId in request.JobIds)
            {
                await using var jobCheck = new SqlCommand("SELECT COUNT(*) FROM dbo.Job WHERE Id=@Id AND IsDeleted=0 AND Status='OPEN'", connection, transaction);
                jobCheck.Parameters.AddWithValue("@Id", jobId);
                if (Convert.ToInt32(await jobCheck.ExecuteScalarAsync(cancellationToken)) != 1)
                    throw new BusinessException("JOB_NOT_OPEN", $"岗位 {jobId} 不存在或未开放。", 409);
                await using var insertJob = new SqlCommand("INSERT dbo.ResumeImportBatchJob(BatchId,JobId) VALUES(@BatchId,@JobId)", connection, transaction);
                insertJob.Parameters.AddWithValue("@BatchId", batchId); insertJob.Parameters.AddWithValue("@JobId", jobId);
                await insertJob.ExecuteNonQueryAsync(cancellationToken);
            }

            const string filesSql = "SELECT Id,FileHash FROM dbo.ResumeImportFile WITH (UPDLOCK,HOLDLOCK) WHERE BatchId=@Id AND Status='REVIEW_READY' ORDER BY Id";
            var files = new List<(long Id, string Hash)>();
            await using (var filesCommand = new SqlCommand(filesSql, connection, transaction))
            {
                filesCommand.Parameters.AddWithValue("@Id", batchId);
                await using var reader = await filesCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) files.Add((reader.GetInt64(0), reader.GetString(1)));
            }
            if (files.Count == 0) throw new BusinessException("NO_REVIEW_READY_FILES", "没有可导入的简历。", 409);
            foreach (var file in files)
            {
                await using var update = new SqlCommand("UPDATE dbo.ResumeImportFile SET Status='QUEUED',ReviewedBy=@UserId,ReviewTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME() WHERE Id=@Id", connection, transaction);
                update.Parameters.AddWithValue("@UserId", userId); update.Parameters.AddWithValue("@Id", file.Id);
                await update.ExecuteNonQueryAsync(cancellationToken);
                await using var task = new SqlCommand("""
                    INSERT dbo.AITask(TaskType,ResumeImportFileId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey,NextRunTime,CreateTime,UpdateTime)
                    VALUES('RESUME_IMPORT',@FileId,'WAITING',5,0,0,@BusinessKey,SYSUTCDATETIME(),SYSUTCDATETIME(),SYSUTCDATETIME())
                    """, connection, transaction);
                task.Parameters.AddWithValue("@FileId", file.Id);
                task.Parameters.AddWithValue("@BusinessKey", $"resume-import:{file.Id}:{file.Hash}");
                await task.ExecuteNonQueryAsync(cancellationToken);
            }
            await using var batchUpdate = new SqlCommand("""
                UPDATE dbo.ResumeImportBatch SET Status='QUEUED',Remark=@Remark,ConfirmedBy=@UserId,
                  ConfirmTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME()
                WHERE Id=@Id AND Status='REVIEWING' AND RowVersion=@RowVersion
                """, connection, transaction);
            batchUpdate.Parameters.AddWithValue("@Id", batchId); batchUpdate.Parameters.AddWithValue("@UserId", userId);
            batchUpdate.Parameters.Add("@Remark", SqlDbType.NVarChar, 500).Value = (object?)request.Remark ?? DBNull.Value;
            batchUpdate.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = rowVersion;
            if (await batchUpdate.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new BusinessException("CONCURRENCY_CONFLICT", "批次已被其他用户修改，请刷新。", 409);
            await transaction.CommitAsync(cancellationToken);
            return new(batchId, files.Count);
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    public async Task CancelReviewAsync(long batchId, long userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var detail = await GetAsync(batchId, cancellationToken) ?? throw new BusinessException("IMPORT_BATCH_NOT_FOUND", "批次不存在。", 404);
        if (detail.Batch.Status != "REVIEWING") throw new BusinessException("IMPORT_BATCH_NOT_REVIEWING", "批次已不处于审阅状态。", 409);
        var ownerId = await GetBatchOwnerAsync(batchId, cancellationToken);
        if (!isAdmin && ownerId != userId) throw new BusinessException("IMPORT_BATCH_FORBIDDEN", "只有批次创建人或管理员可以取消。", 403);
        var paths = await GetReturnPathsAsync(batchId, cancellationToken);
        foreach (var item in paths)
        {
            var from = ResolveRelative(item.TempPath); var to = ResolveRelative(item.OriginalPath);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            if (File.Exists(from)) File.Move(from, MakeUniquePath(to));
        }
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            UPDATE dbo.ResumeImportFile SET Status='RETURNED',UpdateTime=SYSUTCDATETIME() WHERE BatchId=@Id AND Status='REVIEW_READY';
            UPDATE dbo.ResumeImportBatch SET Status='CANCELLED',UpdateTime=SYSUTCDATETIME(),FinishTime=SYSUTCDATETIME() WHERE Id=@Id AND Status='REVIEWING';
            """, connection);
        command.Parameters.AddWithValue("@Id", batchId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ResumeImportFileDto>> ListFailedAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id,SourceType,OriginalFileName,FileExtension,FileSize,Status,ErrorCode,ErrorMessage,DetectedResumeCount,CreateTime
            FROM dbo.ResumeImportFile WHERE Status IN ('FAILED','REJECTED') ORDER BY UpdateTime DESC,Id DESC
            """;
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ResumeImportFileDto>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(MapFile(reader));
        return result;
    }

    public async Task<ResumeImportFailurePreview> GetFailedPreviewAsync(long fileId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT OriginalFileName,FileExtension,ErrorCode,ErrorMessage,TempFilePath FROM dbo.ResumeImportFile WHERE Id=@Id AND Status IN ('FAILED','REJECTED')";
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", fileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new BusinessException("FAILED_FILE_NOT_FOUND", "异常文件不存在或已处理。", 404);
        var name = reader.GetString(0);
        var extension = reader.GetString(1);
        var errorCode = reader.GetNullableString(2);
        var errorMessage = reader.GetNullableString(3);
        if (!extension.Equals("JSON", StringComparison.OrdinalIgnoreCase))
            return new(fileId, name, extension, errorCode, errorMessage, null, false);
        try
        {
            var path = ResolveRelative(reader.GetString(4));
            await using var stream = File.OpenRead(path);
            using var document = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var preview = document.RootElement.TryGetProperty("visible_text", out var text) && text.ValueKind == System.Text.Json.JsonValueKind.String
                ? text.GetString() ?? "" : "JSON中没有可预览的 visible_text。";
            return new(fileId, name, extension, errorCode, errorMessage, preview.Length > 6000 ? preview[..6000] + "\n……（仅显示前6000字符）" : preview, true);
        }
        catch (System.Text.Json.JsonException)
        {
            return new(fileId, name, extension, errorCode, errorMessage, "无法读取JSON正文；该文件可能存在编码或结构问题。", true);
        }
        catch (IOException)
        {
            return new(fileId, name, extension, errorCode, errorMessage, "无法读取JSON正文；该文件可能存在编码或结构问题。", true);
        }
        catch (UnauthorizedAccessException)
        {
            return new(fileId, name, extension, errorCode, errorMessage, "无法读取JSON正文；该文件可能存在编码或结构问题。", true);
        }
    }

    public async Task ReturnFailedAsync(long fileId, string reason, long userId, CancellationToken cancellationToken)
    {
        reason = reason.Trim();
        if (reason.Length is < 1 or > 500) throw new BusinessException("INVALID_RETURN_REASON", "退回原因必须为1至500字。", 400);
        const string select = "SELECT TempFilePath,OriginalSourcePath FROM dbo.ResumeImportFile WHERE Id=@Id AND Status IN ('FAILED','REJECTED')";
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        string tempPath; string originalPath;
        await using (var command = new SqlCommand(select, connection))
        {
            command.Parameters.AddWithValue("@Id", fileId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new BusinessException("FAILED_FILE_NOT_FOUND", "异常文件不存在或已处理。", 404);
            tempPath = reader.GetString(0); originalPath = reader.GetString(1);
        }
        var source = ResolveRelative(tempPath); var target = ResolveRelative(originalPath);
        if (File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(source, MakeUniquePath(target));
        }
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var update = new SqlCommand("UPDATE dbo.ResumeImportFile SET Status='RETURNED',TempFilePath=OriginalSourcePath,ReviewedBy=@UserId,ReviewTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND Status IN ('FAILED','REJECTED')", connection, transaction);
            update.Parameters.AddWithValue("@Id", fileId); update.Parameters.AddWithValue("@UserId", userId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1) throw new BusinessException("FAILED_FILE_NOT_FOUND", "异常文件状态已变化，请刷新。", 409);
            await using var audit = new SqlCommand("INSERT dbo.CleanupLog(ObjectType,ObjectId,Action,Reason,OperatorId,CreateTime) VALUES('RESUME_IMPORT_FILE',@Id,'RETURN_FAILED_IMPORT',@Reason,@UserId,SYSUTCDATETIME())", connection, transaction);
            audit.Parameters.AddWithValue("@Id", fileId); audit.Parameters.AddWithValue("@UserId", userId); audit.Parameters.Add("@Reason", SqlDbType.NVarChar, 500).Value = reason;
            await audit.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    public async Task PurgeFailedAsync(long fileId, string reason, long userId, CancellationToken cancellationToken)
    {
        reason = reason.Trim();
        if (reason.Length is < 1 or > 500) throw new BusinessException("INVALID_PURGE_REASON", "清理原因必须为1至500字。 ");
        const string select = "SELECT TempFilePath FROM dbo.ResumeImportFile WHERE Id=@Id AND Status IN ('FAILED','REJECTED')";
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        string path;
        await using (var command = new SqlCommand(select, connection))
        {
            command.Parameters.AddWithValue("@Id", fileId);
            path = (string?)await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new BusinessException("FAILED_FILE_NOT_FOUND", "异常文件不存在或已清理。", 404);
        }
        var fullPath = ResolveRelative(path);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var update = new SqlCommand("UPDATE dbo.ResumeImportFile SET Status='PURGED',ReviewedBy=@UserId,ReviewTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND Status IN ('FAILED','REJECTED')", connection, transaction);
            update.Parameters.AddWithValue("@Id", fileId); update.Parameters.AddWithValue("@UserId", userId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1) throw new BusinessException("FAILED_FILE_NOT_FOUND", "异常文件状态已改变。", 409);
            await using var audit = new SqlCommand("INSERT dbo.CleanupLog(ObjectType,ObjectId,Action,Reason,OperatorId,CreateTime) VALUES('RESUME_IMPORT_FILE',@Id,'PURGE_FAILED_IMPORT',@Reason,@UserId,SYSUTCDATETIME())", connection, transaction);
            audit.Parameters.AddWithValue("@Id", fileId); audit.Parameters.AddWithValue("@UserId", userId); audit.Parameters.Add("@Reason", SqlDbType.NVarChar, 500).Value = reason;
            await audit.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    private IEnumerable<SourceCandidate> GetCandidates()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-settings.MinimumStableSeconds);
        foreach (var folder in new[] { "ThirdPartyImport" })
        {
            var full = ResolveRelative(folder); Directory.CreateDirectory(full);
            foreach (var path in Directory.EnumerateFiles(full, "*", SearchOption.TopDirectoryOnly)
                         .Where(p => AllowedExtensions.Contains(Path.GetExtension(p)))
                         .Select(p => new FileInfo(p)).Where(f => f.LastWriteTimeUtc <= cutoff && !f.Attributes.HasFlag(FileAttributes.ReparsePoint))
                         .OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                yield return new(folder, path.FullName, path.Name);
        }
    }

    private async Task<(List<SourceCandidate> Items, int RemovedCount)> RemoveAlreadyImportedThirdPartyJsonAsync(
        List<SourceCandidate> candidates, CancellationToken ct)
    {
        var existingTalentIds = await GetExisting51JobTalentIdsAsync(ct);
        if (existingTalentIds.Count == 0) return (candidates, 0);
        var remaining = new List<SourceCandidate>(candidates.Count);
        var removed = 0;
        foreach (var candidate in candidates)
        {
            if (!Path.GetExtension(candidate.FullPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                remaining.Add(candidate);
                continue;
            }
            var talentId = TryReadTalentId(candidate.FullPath);
            if (talentId is null || !existingTalentIds.Contains(talentId))
            {
                remaining.Add(candidate);
                continue;
            }
            try
            {
                File.Delete(candidate.FullPath);
                removed++;
            }
            catch (IOException)
            {
                remaining.Add(candidate);
            }
            catch (UnauthorizedAccessException)
            {
                remaining.Add(candidate);
            }
        }
        return (remaining, removed);
    }

    private async Task<HashSet<string>> GetExisting51JobTalentIdsAsync(CancellationToken ct)
    {
        const string sql = "SELECT TalentId FROM dbo.Resume WHERE IsDeleted=0 AND ResumeSource='51JOB' AND TalentId IS NOT NULL";
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct))
        {
            var talentId = reader.GetString(0).Trim();
            if (talentId.Length > 0) result.Add(talentId);
        }
        return result;
    }

    private static string? TryReadTalentId(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = System.Text.Json.JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("talent_id", out var talentId) || talentId.ValueKind != System.Text.Json.JsonValueKind.String)
                return null;
            var value = talentId.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (System.Text.Json.JsonException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private string ResolveRelative(string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new BusinessException("INVALID_STORAGE_PATH", "存储路径无效。", 500);
        var root = Path.GetFullPath(settings.ResumeTempRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new BusinessException("INVALID_STORAGE_PATH", "存储路径越界。", 500);
        RejectReparsePoints(root, full);
        return full;
    }

    private static void RejectReparsePoints(string root, string fullPath)
    {
        var rootDirectory = new DirectoryInfo(root);
        if (rootDirectory.Exists && rootDirectory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new BusinessException("REPARSE_POINT_FORBIDDEN", "临时目录不能是重解析点。", 500);
        var relative = Path.GetRelativePath(root, fullPath);
        var current = root.TrimEnd(Path.DirectorySeparatorChar);
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if ((Directory.Exists(current) || File.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new BusinessException("REPARSE_POINT_FORBIDDEN", "临时文件路径包含重解析点。", 400);
        }
    }

    private string MoveToFailed(string sourceFullPath, Guid batchKey, string extension)
    {
        var relative = Path.Combine("Failed", batchKey.ToString("N"), $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
        var target = ResolveRelative(relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(sourceFullPath, target); return relative;
    }

    private bool HasValidStructure(string path, string extension, string originalFileName)
    {
        try
        {
            return extension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                   ResumeJsonValidator.IsValid(path, settings.JsonMaxFileBytes, originalFileName);
        }
        catch (InvalidDataException) { return false; }
        catch (IOException) { return false; }
    }

    private static bool HasValidManualStructure(string path, string extension)
    {
        try
        {
            if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                Span<byte> header = stackalloc byte[5];
                using var stream = File.OpenRead(path);
                return stream.Read(header) == header.Length && header.SequenceEqual("%PDF-"u8);
            }
            using var archive = ZipFile.OpenRead(path);
            return archive.GetEntry("[Content_Types].xml") is not null && archive.GetEntry("word/document.xml") is not null;
        }
        catch (InvalidDataException) { return false; }
        catch (IOException) { return false; }
    }

    private static string MakeUniquePath(string path) => !File.Exists(path) ? path :
        Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}-{Guid.NewGuid():N}{Path.GetExtension(path)}");

    private async Task EnsureNoPendingReviewAsync(long userId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT TOP (1) Id FROM dbo.ResumeImportBatch WHERE CreatedBy=@UserId AND Status='REVIEWING' ORDER BY Id DESC", connection);
        command.Parameters.AddWithValue("@UserId", userId);
        var existing = await command.ExecuteScalarAsync(ct);
        if (existing is not null) throw new BusinessException("IMPORT_REVIEW_PENDING", $"已有待审阅批次 {existing}。", 409);
    }

    private async Task<long> CreateBatchAsync(Guid key, long userId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var command = new SqlCommand("INSERT dbo.ResumeImportBatch(BatchKey,CreatedBy) VALUES(@Key,@UserId); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", connection);
        command.Parameters.AddWithValue("@Key", key); command.Parameters.AddWithValue("@UserId", userId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    private async Task InsertFileAsync(long batchId, string sourceType, string name, string extension, string originalPath, string tempPath, long size, string hash, string status, string? errorCode, string? errorMessage, long? externalJobMappingId, long? sourceJobId, CancellationToken ct)
    {
        const string sql = """
            INSERT dbo.ResumeImportFile(BatchId,SourceType,OriginalFileName,FileExtension,OriginalSourcePath,TempFilePath,FileSize,FileHash,Status,ErrorCode,ErrorMessage,ExternalJobMappingId,SourceJobId)
            VALUES(@BatchId,@SourceType,@Name,@Extension,@OriginalPath,@TempPath,@Size,@Hash,@Status,@ErrorCode,@ErrorMessage,@ExternalJobMappingId,@SourceJobId)
            """;
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BatchId", batchId); command.Parameters.AddWithValue("@SourceType", sourceType);
        command.Parameters.Add("@Name", SqlDbType.NVarChar, 260).Value = name; command.Parameters.AddWithValue("@Extension", extension);
        command.Parameters.Add("@OriginalPath", SqlDbType.NVarChar, 1000).Value = originalPath; command.Parameters.Add("@TempPath", SqlDbType.NVarChar, 1000).Value = tempPath;
        command.Parameters.AddWithValue("@Size", size); command.Parameters.AddWithValue("@Hash", hash); command.Parameters.AddWithValue("@Status", status);
        command.Parameters.Add("@ErrorCode", SqlDbType.VarChar, 100).Value = (object?)errorCode ?? DBNull.Value;
        command.Parameters.Add("@ErrorMessage", SqlDbType.NVarChar, 2000).Value = (object?)errorMessage ?? DBNull.Value;
        command.Parameters.AddWithValue("@ExternalJobMappingId", (object?)externalJobMappingId ?? DBNull.Value);
        command.Parameters.AddWithValue("@SourceJobId", (object?)sourceJobId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<(long MappingId, long JobId)?> FindExternalJobMappingAsync(SourceJob sourceJob, CancellationToken ct)
    {
        // Resolve the source trace only after upload precheck mapped the exact JSON job
        // name to an internal Job. External ids are trace metadata, never Job ids.
        const string sql = "SELECT TOP (1) Id,JobId FROM dbo.ExternalJobMapping WHERE Platform=@Platform AND ExternalJobId=@ExternalJobId AND ExternalJobName=@Name ORDER BY Id";
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Platform", sourceJob.Platform);
        command.Parameters.Add("@ExternalJobId", SqlDbType.NVarChar, 100).Value = sourceJob.ExternalJobId;
        command.Parameters.Add("@Name", SqlDbType.NVarChar, 400).Value = sourceJob.ExternalJobName;
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (reader.GetInt64(0), reader.GetInt64(1)) : null;
    }

    private static SourceJob? TryReadSourceJob(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.OpenRead(path));
            if (!document.RootElement.TryGetProperty("source_job", out var value) || value.ValueKind != JsonValueKind.Object) return null;
            var platform = ReadJsonString(value, "provider"); var externalId = ReadJsonString(value, "external_job_id"); var name = ReadJsonString(value, "external_job_name");
            return string.Equals(platform, "51JOB", StringComparison.OrdinalIgnoreCase) && externalId.Length is > 0 and <= 100 && name.Length is > 0 and <= 400
                ? new SourceJob("51JOB", externalId, name) : null;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    private static string ReadJsonString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? string.Empty : string.Empty;
    private readonly record struct SourceJob(string Platform, string ExternalJobId, string ExternalJobName);
    private sealed record ManualSavedFile(string OriginalName, string Extension, string RelativePath, long Length, string Hash, string FullPath);

    private async Task<bool> FormalResumeHashExistsAsync(string hash, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.Resume WHERE FileHash=@Hash AND IsDeleted=0", connection);
        command.Parameters.AddWithValue("@Hash", hash);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) > 0;
    }

    private async Task UpdateBatchCountsAsync(long id, int source, int hr, int third, int failed, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var command = new SqlCommand("UPDATE dbo.ResumeImportBatch SET SourceFileCount=@Source,HrUploadCount=@Hr,ThirdPartyCount=@Third,FailedCount=@Failed,UpdateTime=SYSUTCDATETIME() WHERE Id=@Id", connection);
        command.Parameters.AddWithValue("@Source", source); command.Parameters.AddWithValue("@Hr", hr); command.Parameters.AddWithValue("@Third", third); command.Parameters.AddWithValue("@Failed", failed); command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task DeleteEmptyBatchAsync(long id, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var command = new SqlCommand("DELETE dbo.ResumeImportBatch WHERE Id=@Id AND SourceFileCount=0", connection);
        command.Parameters.AddWithValue("@Id", id); await command.ExecuteNonQueryAsync(ct);
    }

    private async Task MarkBatchFailedAsync(long id, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var command = new SqlCommand("UPDATE dbo.ResumeImportBatch SET Status='FAILED',FinishTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND Status='REVIEWING'", connection);
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<(string Path, Guid BatchKey)?> GetFilePathAsync(long fileId, long batchId, string status, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT f.TempFilePath,b.BatchKey FROM dbo.ResumeImportFile f JOIN dbo.ResumeImportBatch b ON b.Id=f.BatchId WHERE f.Id=@FileId AND f.BatchId=@BatchId AND f.Status=@Status AND b.Status='REVIEWING'", connection);
        command.Parameters.AddWithValue("@FileId", fileId); command.Parameters.AddWithValue("@BatchId", batchId); command.Parameters.AddWithValue("@Status", status);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (reader.GetString(0), reader.GetGuid(1)) : null;
    }

    private async Task ExecuteFileUpdateAsync(string sql, long fileId, long batchId, long userId, string path, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct); await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@FileId", fileId); command.Parameters.AddWithValue("@BatchId", batchId); command.Parameters.AddWithValue("@UserId", userId); command.Parameters.Add("@Path", SqlDbType.NVarChar, 1000).Value = path;
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new BusinessException("CONCURRENCY_CONFLICT", "文件状态已变化，请刷新。", 409);
    }

    private async Task<long> GetBatchOwnerAsync(long id, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct); await using var command = new SqlCommand("SELECT CreatedBy FROM dbo.ResumeImportBatch WHERE Id=@Id", connection); command.Parameters.AddWithValue("@Id", id);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct) ?? throw new BusinessException("IMPORT_BATCH_NOT_FOUND", "批次不存在。", 404));
    }

    private async Task<List<(string TempPath, string OriginalPath)>> GetReturnPathsAsync(long id, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct); await using var command = new SqlCommand("SELECT TempFilePath,OriginalSourcePath FROM dbo.ResumeImportFile WHERE BatchId=@Id AND Status='REVIEW_READY'", connection); command.Parameters.AddWithValue("@Id", id);
        await using var reader = await command.ExecuteReaderAsync(ct); var result = new List<(string, string)>();
        while (await reader.ReadAsync(ct)) result.Add((reader.GetString(0), reader.GetString(1))); return result;
    }

    private static ResumeImportBatchSummary MapBatch(SqlDataReader reader) => new(
        reader.GetInt64(0), reader.GetGuid(1), reader.GetString(2), reader.GetNullableString(3), reader.GetInt32(4),
        reader.GetInt32(5), reader.GetInt32(6), reader.GetString(7), reader.GetDateTime(8), Convert.ToBase64String((byte[])reader[9]));

    private static ResumeImportFileDto MapFile(SqlDataReader reader) => new(
        reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), reader.GetString(5),
        reader.GetNullableString(6), reader.GetNullableString(7), reader.GetNullableInt32(8), reader.GetDateTime(9));

    private sealed record SourceCandidate(string SourceFolder, string FullPath, string OriginalName);
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RecruitAI.Application.Common;
using RecruitAI.Application.Imports;
using RecruitAI.Infrastructure.Database;

namespace RecruitAI.Web.Controllers;

[ApiController]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route("api/third-party-resumes")]
public sealed class ThirdPartyResumesController(IConfiguration configuration, SqlConnectionFactory? connectionFactory = null, IResumeImportService? importService = null) : ControllerBase
{
    private const int MaxJsonBytes = 10 * 1024 * 1024;

    [HttpPost]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBytes)]
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        if (!HasValidApiKey())
            return Unauthorized(new { code = "INVALID_API_KEY", message = "API Key 无效。" });

        await using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
            return BadRequest(new { code = "EMPTY_JSON", message = "JSON 内容不能为空。" });
        if (buffer.Length > MaxJsonBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { code = "JSON_TOO_LARGE", message = "单个 JSON 不能超过 10MB。" });

        var bytes = buffer.ToArray();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            return BadRequest(new { code = "INVALID_JSON", message = ex.Message });
        }

        using (document)
        {
            if (!TryReadTalentId(document.RootElement, out var talentId))
                return BadRequest(new { code = "INVALID_TALENT_ID", message = "talent_id 缺失或格式无效。" });

            if (!TryReadSourceJob(document.RootElement, out var sourceJob))
                return BadRequest(new { code = "INVALID_SOURCE_JOB", message = "source_job（平台、外部岗位 ID、外部岗位名称）缺失或格式无效。" });

            // The web host always supplies the database factory. The null branch keeps the
            // controller's filesystem-only unit tests independent from a SQL Server instance.
            var precheck = connectionFactory is null
                ? new PrecheckResult(false, "STAGED", 0, "未执行数据库预检。")
                : await PrecheckAsync(talentId, sourceJob, cancellationToken);
            if (precheck.NoUpload)
            {
                return Ok(new
                {
                    duplicate = true,
                    no_upload = true,
                    action = precheck.Action,
                    talent_id = talentId,
                    job_id = precheck.JobId,
                    message = precheck.Message
                });
            }

            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var root = configuration["ThirdPartyResumeImport:Directory"]
                       ?? @"C:\Zhaopin\ResumeTemp\ThirdPartyImport";
            Directory.CreateDirectory(root);

            // 同一人才可来自多个外部岗位；临时目录必须保留每个岗位来源。
            var fileName = $"51JOB_{talentId}_{ToSafeFileToken(sourceJob.ExternalJobId)}.json";
            var path = Path.Combine(root, fileName);
            if (System.IO.File.Exists(path))
                return Ok(new { duplicate = true, file_name = fileName, sha256 = hash });

            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".uploading";
            try
            {
                await System.IO.File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
                System.IO.File.Move(temporaryPath, path, overwrite: false);

                // The HTTP body has already been written completely to a temporary file and
                // atomically renamed. Mark this completed upload as stable so the client's
                // immediate JSON scan can include it after the atomic rename.
                var stableSeconds = Math.Max(0,
                    configuration.GetValue("ResumeImport:MinimumStableSeconds", 30));
                System.IO.File.SetLastWriteTimeUtc(
                    path,
                    DateTime.UtcNow.AddSeconds(-(stableSeconds + 1)));
            }
            catch (IOException) when (System.IO.File.Exists(path))
            {
                if (System.IO.File.Exists(temporaryPath)) System.IO.File.Delete(temporaryPath);
                return Ok(new { duplicate = true, file_name = fileName, sha256 = hash });
            }

            return Created($"/api/third-party-resumes/{fileName}",
                new { duplicate = false, no_upload = false, action = "STAGED", file_name = fileName, sha256 = hash, job_id = precheck.JobId });
        }
    }

    [HttpPost("scan")]
    public async Task<IActionResult> Scan(CancellationToken cancellationToken)
    {
        if (!HasValidApiKey()) return Unauthorized(new { code = "INVALID_API_KEY", message = "API Key 无效。" });
        if (importService is null) return StatusCode(503, new { code = "IMPORT_SERVICE_UNAVAILABLE", message = "简历扫描服务未就绪。" });
        var userId = configuration.GetValue<long?>("ThirdPartyResumeImport:ScanUserId") ?? 1L;
        try
        {
            var result = await importService.ScanAsync(userId, cancellationToken);
            return Ok(new { status = result.Result, batch_id = result.BatchId, review_ready_count = result.ReviewReadyCount, message = result.Message });
        }
        catch (BusinessException ex) when (ex.Code == "IMPORT_REVIEW_PENDING")
        {
            return Conflict(new { code = ex.Code, message = "已有待 HR 审阅的导入批次；本次文件会留在临时目录，待该批次处理后自动扫描。" });
        }
    }

    private async Task<PrecheckResult> PrecheckAsync(string talentId, SourceJob sourceJob, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory!.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var mapping = await GetOrCreateMappingAsync(connection, transaction, sourceJob, cancellationToken);
            long? resumeId = null;
            long? candidateId = null;
            await using (var command = new SqlCommand("SELECT TOP (1) Id,CandidateId FROM dbo.Resume WITH (UPDLOCK,HOLDLOCK) WHERE IsDeleted=0 AND ResumeSource='51JOB' AND TalentId=@TalentId ORDER BY Id DESC", connection, transaction))
            {
                command.Parameters.Add("@TalentId", System.Data.SqlDbType.NVarChar, 200).Value = talentId;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    resumeId = reader.GetInt64(0);
                    candidateId = reader.GetInt64(1);
                }
            }

            if (resumeId is null || candidateId is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new(false, "STAGED", mapping.JobId, "人才尚未入库，上传完整简历等待导入。");
            }

            var isAlreadyRelated = await RelationExistsAsync(connection, transaction, resumeId.Value, mapping.Id, cancellationToken);
            if (!isAlreadyRelated)
            {
                await using (var addSource = new SqlCommand("INSERT dbo.ResumeJobSource(ResumeId,JobId,ExternalJobMappingId) VALUES(@ResumeId,@JobId,@MappingId)", connection, transaction))
                {
                    addSource.Parameters.AddWithValue("@ResumeId", resumeId.Value); addSource.Parameters.AddWithValue("@JobId", mapping.JobId); addSource.Parameters.AddWithValue("@MappingId", mapping.Id);
                    await addSource.ExecuteNonQueryAsync(cancellationToken);
                }
                await using var addCandidateJob = new SqlCommand("IF NOT EXISTS(SELECT 1 FROM dbo.CandidateJob WITH (UPDLOCK,HOLDLOCK) WHERE CandidateId=@CandidateId AND JobId=@JobId) INSERT dbo.CandidateJob(CandidateId,JobId) VALUES(@CandidateId,@JobId)", connection, transaction);
                addCandidateJob.Parameters.AddWithValue("@CandidateId", candidateId.Value); addCandidateJob.Parameters.AddWithValue("@JobId", mapping.JobId);
                await addCandidateJob.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var ensureParse = new SqlCommand("IF EXISTS(SELECT 1 FROM dbo.Resume WHERE Id=@ResumeId AND IsDeleted=0 AND IsCurrent=1 AND IsAiAnalyzed=0) AND NOT EXISTS(SELECT 1 FROM dbo.AITask WHERE TaskType='RESUME_PARSE' AND ResumeId=@ResumeId) INSERT dbo.AITask(TaskType,ResumeId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey) VALUES('RESUME_PARSE',@ResumeId,'WAITING',4,0,3,'resume-parse:'+CONVERT(VARCHAR(30),@ResumeId))", connection, transaction))
            {
                ensureParse.Parameters.AddWithValue("@ResumeId", resumeId.Value);
                await ensureParse.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return new(true, isAlreadyRelated ? "ALREADY_RELATED" : "RELATED", mapping.JobId,
                isAlreadyRelated ? "人才与当前岗位已关联，无需上传。" : "已补充人才与当前岗位关联，无需上传。 ");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<(long Id, long JobId)> GetOrCreateMappingAsync(SqlConnection connection, SqlTransaction transaction, SourceJob sourceJob, CancellationToken ct)
    {
        long jobId;
        var mappingStatus = "CONFIRMED";
        // The internal Job is a movable resume category. Select it only by the exact,
        // trimmed job name; the provider's external id is retained for traceability only.
        await using (var findJob = new SqlCommand("SELECT TOP (1) Id FROM dbo.Job WHERE IsDeleted=0 AND Name=@Name ORDER BY Id", connection, transaction))
        {
            findJob.Parameters.Add("@Name", System.Data.SqlDbType.NVarChar, 400).Value = sourceJob.ExternalJobName;
            var existing = await findJob.ExecuteScalarAsync(ct);
            if (existing is not null) jobId = Convert.ToInt64(existing);
            else
            {
                mappingStatus = "PENDING_HR";
                await using var createJob = new SqlCommand("INSERT dbo.Job(Name,Requirement,Status,CreatedBy) SELECT @Name,N'外部职位自动建立，请 HR 完善岗位说明和要求。','DRAFT',Id FROM dbo.AppUser WHERE UserName='admin'; SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", connection, transaction);
                createJob.Parameters.Add("@Name", System.Data.SqlDbType.NVarChar, 400).Value = sourceJob.ExternalJobName;
                jobId = Convert.ToInt64(await createJob.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("ADMIN user is missing."));
                await using var createScore = new SqlCommand("INSERT dbo.ScoreConfig(JobId,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,DutyWeight,ProductWeight,IndustryWeight,TenureWeight,CompanyEnvironmentWeight,Version,UpdatedBy) VALUES(@JobId,30,25,35,10,40,25,15,10,10,1,(SELECT Id FROM dbo.AppUser WHERE UserName='admin'))", connection, transaction);
                createScore.Parameters.AddWithValue("@JobId", jobId);
                await createScore.ExecuteNonQueryAsync(ct);
                await using var createScoreHistory = new SqlCommand("INSERT dbo.ScoreConfigHistory(JobId,Version,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,RuleJson,UpdatedBy,DutyWeight,ProductWeight,IndustryWeight,TenureWeight,CompanyEnvironmentWeight) SELECT JobId,Version,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,RuleJson,UpdatedBy,DutyWeight,ProductWeight,IndustryWeight,TenureWeight,CompanyEnvironmentWeight FROM dbo.ScoreConfig WHERE JobId=@JobId",connection,transaction);
                createScoreHistory.Parameters.AddWithValue("@JobId",jobId);await createScoreHistory.ExecuteNonQueryAsync(ct);
                var profileContent = $"岗位名称：{sourceJob.ExternalJobName}\r\n岗位职责：待HR完善\r\n核心技能：待HR完善\r\n产品与项目经验：待HR完善\r\n行业经验：待HR完善\r\n学历要求：待HR完善\r\n加分项：待HR完善\r\n排除项：待HR完善";
                var profileHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileContent.Replace("\r\n", "\n"))));
                await using var createProfile = new SqlCommand("DECLARE @AdminId BIGINT=(SELECT Id FROM dbo.AppUser WHERE UserName='admin'); INSERT dbo.KnowledgeDocument(Code,Title,DocumentType,Content,Status,ContentHash,Version,CreatedBy,IsDeleted) VALUES(@Code,@Name,'JOB_REQUIREMENT',@Content,'DRAFT',@Hash,1,@AdminId,0); DECLARE @DocumentId BIGINT=SCOPE_IDENTITY(); INSERT dbo.JobKnowledgeDocument(JobId,KnowledgeDocumentId,RelationType,IsPrimary,Priority,CreatedBy) VALUES(@JobId,@DocumentId,'PRIMARY_REQUIREMENT',1,100,@AdminId);", connection, transaction);
                createProfile.Parameters.AddWithValue("@JobId",jobId);createProfile.Parameters.Add("@Code",System.Data.SqlDbType.NVarChar,100).Value=$"JOB-PROFILE-{jobId}";createProfile.Parameters.Add("@Name",System.Data.SqlDbType.NVarChar,300).Value=sourceJob.ExternalJobName;createProfile.Parameters.Add("@Content",System.Data.SqlDbType.NVarChar,-1).Value=profileContent;createProfile.Parameters.Add("@Hash",System.Data.SqlDbType.Char,64).Value=profileHash;
                await createProfile.ExecuteNonQueryAsync(ct);
            }
        }

        // External ids are not internal job identifiers and may collide between providers.
        // JobId has already been resolved exclusively by the exact internal Job.Name above.
        // The provider/id/name tuple only identifies this source trace record.
        await using (var select = new SqlCommand("SELECT TOP (1) Id FROM dbo.ExternalJobMapping WITH (UPDLOCK,HOLDLOCK) WHERE Platform=@Platform AND ExternalJobId=@ExternalJobId AND ExternalJobName=@Name AND JobId=@JobId ORDER BY Id", connection, transaction))
        {
            select.Parameters.AddWithValue("@Platform", sourceJob.Platform);
            select.Parameters.Add("@ExternalJobId", System.Data.SqlDbType.NVarChar, 100).Value = sourceJob.ExternalJobId;
            select.Parameters.Add("@Name", System.Data.SqlDbType.NVarChar, 400).Value = sourceJob.ExternalJobName;
            select.Parameters.AddWithValue("@JobId", jobId);
            var existing = await select.ExecuteScalarAsync(ct);
            if (existing is not null)
                return (Convert.ToInt64(existing), jobId);
        }

        await using var createMapping = new SqlCommand("INSERT dbo.ExternalJobMapping(Platform,ExternalJobId,ExternalJobName,JobId,MappingStatus) VALUES(@Platform,@ExternalJobId,@ExternalJobName,@JobId,@Status); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", connection, transaction);
        createMapping.Parameters.AddWithValue("@Platform", sourceJob.Platform); createMapping.Parameters.AddWithValue("@ExternalJobId", sourceJob.ExternalJobId);
        createMapping.Parameters.Add("@ExternalJobName", System.Data.SqlDbType.NVarChar, 400).Value = sourceJob.ExternalJobName;
        createMapping.Parameters.AddWithValue("@JobId", jobId); createMapping.Parameters.AddWithValue("@Status", mappingStatus);
        var mappingId = Convert.ToInt64(await createMapping.ExecuteScalarAsync(ct));
        return (mappingId, jobId);
    }

    private static async Task<bool> RelationExistsAsync(SqlConnection connection, SqlTransaction transaction, long resumeId, long mappingId, CancellationToken ct)
    {
        await using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.ResumeJobSource WITH (UPDLOCK,HOLDLOCK) WHERE ResumeId=@ResumeId AND ExternalJobMappingId=@MappingId", connection, transaction);
        command.Parameters.AddWithValue("@ResumeId", resumeId); command.Parameters.AddWithValue("@MappingId", mappingId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) > 0;
    }

    private bool HasValidApiKey()
    {
        var expected = configuration["ThirdPartyResumeImport:ApiKey"];
        var supplied = Request.Headers["X-API-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(supplied)) return false;
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    private static bool TryReadTalentId(JsonElement root, out string talentId)
    {
        talentId = string.Empty;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("talent_id", out var value))
            return false;
        talentId = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
        return talentId.Length is > 0 and <= 64 &&
               talentId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }

    private static bool TryReadSourceJob(JsonElement root, out SourceJob sourceJob)
    {
        sourceJob = default;
        if (!root.TryGetProperty("source_job", out var value) || value.ValueKind != JsonValueKind.Object) return false;
        var platform = ReadString(value, "provider");
        var externalJobId = ReadString(value, "external_job_id");
        var externalJobName = ReadString(value, "external_job_name");
        if (!string.Equals(platform, "51JOB", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(externalJobId) || string.IsNullOrWhiteSpace(externalJobName) || externalJobId.Length > 100 || externalJobName.Length > 400) return false;
        sourceJob = new("51JOB", externalJobId, externalJobName);
        return true;
    }

    private static string ReadString(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? string.Empty : string.Empty;
    private static string ToSafeFileToken(string value) => string.Concat(value.Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
    private readonly record struct SourceJob(string Platform, string ExternalJobId, string ExternalJobName);
    private readonly record struct PrecheckResult(bool NoUpload, string Action, long JobId, string Message);
}

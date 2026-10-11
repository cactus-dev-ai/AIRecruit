using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RecruitAI.Application.Common;
using RecruitAI.Application.Recruitment;
using RecruitAI.Infrastructure.Database;

namespace RecruitAI.Infrastructure.Recruitment;

public sealed class SqlRecruitmentService(SqlConnectionFactory connectionFactory) : IRecruitmentService
{
    private static readonly HashSet<string> JobStatuses = new(StringComparer.Ordinal) { "DRAFT", "OPEN", "CLOSED", "ARCHIVED" };

    public async Task<IReadOnlyList<JobDto>> ListJobsAsync(int page, int pageSize, string? status, string? keyword, CancellationToken ct)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100); status = status?.Trim().ToUpperInvariant(); keyword = keyword?.Trim();
        const string sql = """
            WITH Paged AS (
              SELECT j.Id,j.Name,j.Description,j.Requirement,j.SkillRequirement,j.MinExperienceMonths,j.EducationRequirement,j.Status,
                s.SkillWeight,s.ProjectWeight,s.ExperienceWeight,s.EducationWeight,s.Version,j.RowVersion,j.UpdateTime,
                COUNT(DISTINCT r.Id) ResumeCount,
                s.DutyWeight,s.ProductWeight,s.IndustryWeight,s.TenureWeight,s.CompanyEnvironmentWeight,
                j.MaxCandidateAge,j.MaxExpectedMonthlySalary,
                ROW_NUMBER() OVER(ORDER BY COUNT(DISTINCT r.Id) DESC,j.Id DESC) RowNo
              FROM dbo.Job j JOIN dbo.ScoreConfig s ON s.JobId=j.Id
              LEFT JOIN dbo.CandidateJob cj ON cj.JobId=j.Id AND cj.Status<>'REJECTED'
              LEFT JOIN dbo.Resume r ON r.CandidateId=cj.CandidateId AND r.IsCurrent=1 AND r.IsDeleted=0
              WHERE j.IsDeleted=0 AND (@Status IS NULL OR j.Status=@Status) AND (@Keyword IS NULL OR j.Name LIKE N'%'+@Keyword+N'%')
              GROUP BY j.Id,j.Name,j.Description,j.Requirement,j.SkillRequirement,j.MinExperienceMonths,j.EducationRequirement,j.Status,
                s.SkillWeight,s.ProjectWeight,s.ExperienceWeight,s.EducationWeight,s.Version,j.RowVersion,j.UpdateTime,
                s.DutyWeight,s.ProductWeight,s.IndustryWeight,s.TenureWeight,s.CompanyEnvironmentWeight,
                j.MaxCandidateAge,j.MaxExpectedMonthlySalary
            ) SELECT Id,Name,Description,Requirement,SkillRequirement,MinExperienceMonths,EducationRequirement,Status,
                SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,Version,RowVersion,UpdateTime,ResumeCount,
                DutyWeight,ProductWeight,IndustryWeight,TenureWeight,CompanyEnvironmentWeight,
                MaxCandidateAge,MaxExpectedMonthlySalary
              FROM Paged WHERE RowNo BETWEEN @Start AND @End ORDER BY RowNo
            """;
        await using var connection = await connectionFactory.OpenAsync(ct); await using var command = new SqlCommand(sql, connection);
        AddNullable(command, "@Status", SqlDbType.VarChar, 20, status); AddNullable(command, "@Keyword", SqlDbType.NVarChar, 200, keyword);
        command.Parameters.AddWithValue("@Start", (page - 1) * pageSize + 1); command.Parameters.AddWithValue("@End", page * pageSize);
        await using var reader = await command.ExecuteReaderAsync(ct); var result = new List<JobDto>();
        while (await reader.ReadAsync(ct)) result.Add(MapJob(reader)); return result;
    }

    public async Task<JobDto?> GetJobAsync(long id, CancellationToken ct)
    {
        const string sql = """
            SELECT j.Id,j.Name,j.Description,j.Requirement,j.SkillRequirement,j.MinExperienceMonths,j.EducationRequirement,j.Status,
              s.SkillWeight,s.ProjectWeight,s.ExperienceWeight,s.EducationWeight,s.Version,j.RowVersion,j.UpdateTime,
              COUNT(DISTINCT r.Id) ResumeCount,s.DutyWeight,s.ProductWeight,s.IndustryWeight,s.TenureWeight,s.CompanyEnvironmentWeight,
              j.MaxCandidateAge,j.MaxExpectedMonthlySalary
            FROM dbo.Job j JOIN dbo.ScoreConfig s ON s.JobId=j.Id
            LEFT JOIN dbo.CandidateJob cj ON cj.JobId=j.Id AND cj.Status<>'REJECTED'
            LEFT JOIN dbo.Resume r ON r.CandidateId=cj.CandidateId AND r.IsCurrent=1 AND r.IsDeleted=0
            WHERE j.Id=@Id AND j.IsDeleted=0
            GROUP BY j.Id,j.Name,j.Description,j.Requirement,j.SkillRequirement,j.MinExperienceMonths,j.EducationRequirement,j.Status,
              s.SkillWeight,s.ProjectWeight,s.ExperienceWeight,s.EducationWeight,s.Version,j.RowVersion,j.UpdateTime,
              s.DutyWeight,s.ProductWeight,s.IndustryWeight,s.TenureWeight,s.CompanyEnvironmentWeight,
              j.MaxCandidateAge,j.MaxExpectedMonthlySalary
            """;
        await using var connection = await connectionFactory.OpenAsync(ct); await using var command = new SqlCommand(sql, connection); command.Parameters.AddWithValue("@Id", id);
        await using var reader = await command.ExecuteReaderAsync(ct); return await reader.ReadAsync(ct) ? MapJob(reader) : null;
    }

    public async Task<long> CreateJobAsync(SaveJobRequest request, long userId, CancellationToken ct)
    {
        ValidateJob(request); await using var connection = await connectionFactory.OpenAsync(ct); await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            await using var insert = new SqlCommand("""
                INSERT dbo.Job(Name,Description,Requirement,SkillRequirement,MinExperienceMonths,EducationRequirement,Status,CreatedBy,IsDeleted)
                VALUES(@Name,@Description,@Requirement,@Skill,@Months,@Education,'DRAFT',@UserId,0); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
                """, connection, transaction); AddJobParameters(insert, request); insert.Parameters.AddWithValue("@UserId", userId);
            var id = Convert.ToInt64(await insert.ExecuteScalarAsync(ct));
            await using var score = new SqlCommand("INSERT dbo.ScoreConfig(JobId,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,DutyWeight,ProductWeight,IndustryWeight,TenureWeight,CompanyEnvironmentWeight,Version,UpdatedBy) VALUES(@Id,30,25,35,10,40,25,15,10,10,1,@UserId); INSERT dbo.ScoreConfigHistory(JobId,Version,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,RuleJson,UpdatedBy,DutyWeight,ProductWeight,IndustryWeight,TenureWeight,CompanyEnvironmentWeight) VALUES(@Id,1,30,25,35,10,NULL,@UserId,40,25,15,10,10)", connection, transaction);
            score.Parameters.AddWithValue("@Id", id); score.Parameters.AddWithValue("@UserId", userId); await score.ExecuteNonQueryAsync(ct);
            var profileContent = BuildInitialProfile(request.Name, request.Requirement);
            await using var profile = new SqlCommand("INSERT dbo.KnowledgeDocument(Code,Title,DocumentType,Content,Status,ContentHash,Version,CreatedBy,IsDeleted) VALUES(@Code,@Title,'JOB_REQUIREMENT',@Content,'DRAFT',@Hash,1,@UserId,0); DECLARE @DocumentId BIGINT=SCOPE_IDENTITY(); INSERT dbo.JobKnowledgeDocument(JobId,KnowledgeDocumentId,RelationType,IsPrimary,Priority,CreatedBy) VALUES(@Id,@DocumentId,'PRIMARY_REQUIREMENT',1,100,@UserId);", connection, transaction);
            profile.Parameters.AddWithValue("@Id", id); profile.Parameters.AddWithValue("@UserId", userId); profile.Parameters.Add("@Code",SqlDbType.NVarChar,100).Value=$"JOB-PROFILE-{id}"; profile.Parameters.Add("@Title",SqlDbType.NVarChar,300).Value=request.Name.Trim(); profile.Parameters.Add("@Content",SqlDbType.NVarChar,-1).Value=profileContent; profile.Parameters.Add("@Hash",SqlDbType.Char,64).Value=HashContent(profileContent); await profile.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct); return id;
        }
        catch { await transaction.RollbackAsync(ct); throw; }
    }

    public async Task UpdateJobAsync(long id, SaveJobRequest request, CancellationToken ct)
    {
        ValidateJob(request); var version = ParseVersion(request.RowVersion);
        const string sql = """
            UPDATE dbo.Job SET Name=@Name,Description=@Description,Requirement=@Requirement,SkillRequirement=@Skill,
              MinExperienceMonths=@Months,EducationRequirement=@Education,UpdateTime=SYSUTCDATETIME()
            WHERE Id=@Id AND IsDeleted=0 AND Status IN ('DRAFT','OPEN') AND RowVersion=@Version
            """;
        await using var connection = await connectionFactory.OpenAsync(ct); await using var command = new SqlCommand(sql, connection); AddJobParameters(command, request);
        command.Parameters.AddWithValue("@Id", id); command.Parameters.Add("@Version", SqlDbType.Timestamp).Value = version;
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new BusinessException("CONCURRENCY_CONFLICT", "岗位已变化或当前状态不可编辑。", 409);
        await using var renameProfile = new SqlCommand("UPDATE d SET Title=@Name,UpdateTime=SYSUTCDATETIME() FROM dbo.KnowledgeDocument d JOIN dbo.JobKnowledgeDocument x ON x.KnowledgeDocumentId=d.Id WHERE x.JobId=@Id AND x.IsPrimary=1 AND x.RelationType='PRIMARY_REQUIREMENT' AND d.IsDeleted=0", connection);
        renameProfile.Parameters.AddWithValue("@Id",id);renameProfile.Parameters.Add("@Name",SqlDbType.NVarChar,300).Value=request.Name.Trim();await renameProfile.ExecuteNonQueryAsync(ct);
    }

    public async Task ChangeJobStatusAsync(long id, string action, CancellationToken ct)
    {
        action = action.ToUpperInvariant();
        var (from, to) = action switch { "OPEN" => ("DRAFT,CLOSED", "OPEN"), "CLOSE" => ("OPEN", "CLOSED"), _ => throw new BusinessException("INVALID_JOB_ACTION", "岗位状态操作无效。") };
        var sql = action == "OPEN"
            ? "UPDATE dbo.Job SET Status='OPEN',UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND IsDeleted=0 AND Status IN ('DRAFT','CLOSED')"
            : "UPDATE dbo.Job SET Status='CLOSED',UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND IsDeleted=0 AND Status='OPEN'";
        await using var connection = await connectionFactory.OpenAsync(ct); await using var command = new SqlCommand(sql, connection); command.Parameters.AddWithValue("@Id", id);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new BusinessException("JOB_STATUS_CONFLICT", $"岗位不能从{from}执行{to}。", 409);
    }

    public async Task DeleteJobAsync(long id, CancellationToken ct)
    {
        const string sql = """
            SET NOCOUNT ON;
            IF NOT EXISTS(SELECT 1 FROM dbo.Job WHERE Id=@Id AND IsDeleted=0)
              SELECT 0;
            ELSE IF EXISTS(
              SELECT 1
              FROM dbo.CandidateJob cj
              JOIN dbo.Resume r ON r.CandidateId=cj.CandidateId AND r.IsCurrent=1 AND r.IsDeleted=0
              WHERE cj.JobId=@Id AND cj.Status<>'REJECTED'
            )
              SELECT -1;
            ELSE BEGIN
              UPDATE dbo.Job
              SET IsDeleted=1,Status='ARCHIVED',UpdateTime=SYSUTCDATETIME()
              WHERE Id=@Id AND IsDeleted=0;
              SELECT 1;
            END
            """;
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        if (result == -1)
            throw new BusinessException("JOB_HAS_RESUMES", "该岗位已关联简历，不能删除；请先合并岗位或处理关联简历。", 409);
        if (result == 0)
            throw new BusinessException("JOB_NOT_FOUND", "岗位不存在或已删除。", 404);
    }

    public async Task UpdateJobCleanupRulesAsync(long id, UpdateJobCleanupRulesRequest request, CancellationToken ct)
    {
        if (request.MaxCandidateAge is < 16 or > 80)
            throw new BusinessException("JOB_MAX_AGE_INVALID", "年龄上限必须在16至80岁之间。");
        if (request.MaxExpectedMonthlySalary is <= 0 or > 1000000)
            throw new BusinessException("JOB_MAX_SALARY_INVALID", "月薪上限必须大于0且不超过100万元。");
        var version = ParseVersion(request.RowVersion);
        const string sql = """
            UPDATE dbo.Job SET MaxCandidateAge=@Age,MaxExpectedMonthlySalary=@Salary,UpdateTime=SYSUTCDATETIME()
            WHERE Id=@Id AND IsDeleted=0 AND RowVersion=@Version
            """;
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@Age", (object?)request.MaxCandidateAge ?? DBNull.Value);
        var salary = command.Parameters.Add("@Salary", SqlDbType.Decimal); salary.Precision = 12; salary.Scale = 2; salary.Value = (object?)request.MaxExpectedMonthlySalary ?? DBNull.Value;
        command.Parameters.Add("@Version", SqlDbType.Timestamp).Value = version;
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new BusinessException("CONCURRENCY_CONFLICT", "岗位已被修改，请刷新后重试。", 409);
    }

    public async Task<JobCleanupResult> CleanupJobCandidatesAsync(long id, string criterion, long userId, CancellationToken ct)
    {
        criterion = criterion.Trim().ToUpperInvariant();
        if (criterion is not ("AGE" or "SALARY"))
            throw new BusinessException("JOB_CLEANUP_CRITERION_INVALID", "清理条件只能是年龄或期望薪酬。");

        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            string jobName; int? maxAge; decimal? maxSalary;
            await using (var job = new SqlCommand("SELECT Name,MaxCandidateAge,MaxExpectedMonthlySalary FROM dbo.Job WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id AND IsDeleted=0", connection, transaction))
            {
                job.Parameters.AddWithValue("@Id", id);
                await using var reader = await job.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) throw new BusinessException("JOB_NOT_FOUND", "岗位不存在。", 404);
                jobName = reader.GetString(0); maxAge = reader.GetNullableInt32(1); maxSalary = reader.GetNullableDecimal(2);
            }
            if (criterion == "AGE" && maxAge is null)
                throw new BusinessException("JOB_MAX_AGE_REQUIRED", "请先设置年龄上限。");
            if (criterion == "SALARY" && maxSalary is null)
                throw new BusinessException("JOB_MAX_SALARY_REQUIRED", "请先设置月薪上限。");

            var candidates = new List<(long CandidateJobId, string? RawText, string Status, bool IsFavorite, DateTime? FavoriteTime, long? FavoriteBy)>();
            const string candidateSql = """
                SELECT cj.Id,r.RawText,cj.Status,cj.IsFavorite,cj.FavoriteTime,cj.FavoriteBy
                FROM dbo.CandidateJob cj
                JOIN dbo.Candidate c ON c.Id=cj.CandidateId AND c.IsDeleted=0
                LEFT JOIN dbo.Resume r ON r.CandidateId=c.Id AND r.IsCurrent=1 AND r.IsDeleted=0
                WHERE cj.JobId=@Id AND cj.Status<>'REJECTED'
                """;
            await using (var command = new SqlCommand(candidateSql, connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", id);
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) candidates.Add((reader.GetInt64(0), reader.GetNullableString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetNullableDateTime(4), reader.GetNullableInt64(5)));
            }

            var rejected = new List<long>(); var unknown = 0;
            foreach (var candidate in candidates)
            {
                if (criterion == "AGE")
                {
                    var value = ParseCandidateAge(candidate.RawText);
                    if (value is null) unknown++; else if (value > maxAge) rejected.Add(candidate.CandidateJobId);
                }
                else
                {
                    var value = ParseExpectedMonthlySalaryUpper(candidate.RawText);
                    if (value is null) unknown++; else if (value > maxSalary) rejected.Add(candidate.CandidateJobId);
                }
            }

            var cleanupBatchId = Guid.NewGuid();
            const string rejectSql = """
                INSERT dbo.JobCandidateCleanupHistory(CleanupBatchId,JobId,CandidateJobId,Criterion,PreviousStatus,
                    PreviousIsFavorite,PreviousFavoriteTime,PreviousFavoriteBy,CleanupBy)
                SELECT @BatchId,@JobId,Id,@Criterion,Status,IsFavorite,FavoriteTime,FavoriteBy,@UserId
                FROM dbo.CandidateJob WHERE Id=@CandidateJobId AND JobId=@JobId AND Status<>'REJECTED';
                UPDATE dbo.CandidateJob SET Status='REJECTED',IsFavorite=0,FavoriteTime=NULL,FavoriteBy=NULL,UpdateTime=SYSUTCDATETIME()
                WHERE Id=@CandidateJobId AND JobId=@JobId AND Status<>'REJECTED'
                """;
            foreach (var candidateJobId in rejected)
            {
                await using var reject = new SqlCommand(rejectSql, connection, transaction);
                reject.Parameters.AddWithValue("@CandidateJobId", candidateJobId); reject.Parameters.AddWithValue("@JobId", id);
                reject.Parameters.AddWithValue("@BatchId", cleanupBatchId); reject.Parameters.AddWithValue("@Criterion", criterion); reject.Parameters.AddWithValue("@UserId", userId);
                await reject.ExecuteNonQueryAsync(ct);
            }

            await using (var audit = new SqlCommand("INSERT dbo.CleanupLog(ObjectType,ObjectId,Action,Reason,OperatorId,DetailJson) VALUES('JOB',@Id,@Action,@Reason,@UserId,@Detail)", connection, transaction))
            {
                audit.Parameters.AddWithValue("@Id", id); audit.Parameters.AddWithValue("@UserId", userId);
                audit.Parameters.Add("@Action", SqlDbType.VarChar, 50).Value = criterion == "AGE" ? "REJECT_OVER_AGE" : "REJECT_OVER_SALARY";
                audit.Parameters.Add("@Reason", SqlDbType.NVarChar, 500).Value = criterion == "AGE" ? $"年龄超过{maxAge}岁" : $"期望月薪超过{maxSalary:0.##}元";
                audit.Parameters.Add("@Detail", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(new { jobId = id, jobName, criterion, maxAge, maxExpectedMonthlySalary = maxSalary, scannedCount = candidates.Count, cleanedCount = rejected.Count, unknownCount = unknown });
                await audit.ExecuteNonQueryAsync(ct);
            }
            await using (var touch = new SqlCommand("UPDATE dbo.Job SET UpdateTime=SYSUTCDATETIME() WHERE Id=@Id", connection, transaction))
            { touch.Parameters.AddWithValue("@Id", id); await touch.ExecuteNonQueryAsync(ct); }

            await transaction.CommitAsync(ct);
            return new(candidates.Count, rejected.Count, unknown);
        }
        catch { await transaction.RollbackAsync(ct); throw; }
    }

    public async Task<JobCleanupRestoreResult> RestoreJobCandidatesAsync(long id, long userId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            await using (var exists = new SqlCommand("SELECT COUNT(*) FROM dbo.Job WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id AND IsDeleted=0", connection, transaction))
            { exists.Parameters.AddWithValue("@Id", id); if (Convert.ToInt32(await exists.ExecuteScalarAsync(ct)) == 0) throw new BusinessException("JOB_NOT_FOUND", "岗位不存在。", 404); }

            const string sql = """
                DECLARE @Restored TABLE(HistoryId BIGINT NOT NULL);
                UPDATE cj SET Status=h.PreviousStatus,IsFavorite=h.PreviousIsFavorite,
                    FavoriteTime=h.PreviousFavoriteTime,FavoriteBy=h.PreviousFavoriteBy,UpdateTime=SYSUTCDATETIME()
                OUTPUT h.Id INTO @Restored(HistoryId)
                FROM dbo.CandidateJob cj
                JOIN dbo.JobCandidateCleanupHistory h ON h.CandidateJobId=cj.Id AND h.JobId=@JobId AND h.IsRestored=0
                WHERE cj.JobId=@JobId AND cj.Status='REJECTED';
                UPDATE h SET IsRestored=1,RestoredBy=@UserId,RestoredTime=SYSUTCDATETIME()
                FROM dbo.JobCandidateCleanupHistory h JOIN @Restored r ON r.HistoryId=h.Id;
                SELECT COUNT(*) FROM @Restored;
                """;
            int restored;
            await using (var restore = new SqlCommand(sql, connection, transaction))
            {
                restore.Parameters.AddWithValue("@JobId", id); restore.Parameters.AddWithValue("@UserId", userId);
                restored = Convert.ToInt32(await restore.ExecuteScalarAsync(ct));
            }
            var legacyRestored = await RestoreLegacyJobCleanupAsync(id, userId, connection, transaction, ct);
            restored += legacyRestored;
            await using (var audit = new SqlCommand("INSERT dbo.CleanupLog(ObjectType,ObjectId,Action,Reason,OperatorId,DetailJson) VALUES('JOB',@Id,'RESTORE_JOB_CANDIDATES',N'恢复岗位一键清理的候选人',@UserId,@Detail); UPDATE dbo.Job SET UpdateTime=SYSUTCDATETIME() WHERE Id=@Id", connection, transaction))
            {
                audit.Parameters.AddWithValue("@Id", id); audit.Parameters.AddWithValue("@UserId", userId);
                audit.Parameters.Add("@Detail", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(new { jobId = id, restoredCount = restored, legacyRestoredCount = legacyRestored });
                await audit.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return new(restored, legacyRestored);
        }
        catch { await transaction.RollbackAsync(ct); throw; }
    }

    private static async Task<int> RestoreLegacyJobCleanupAsync(long jobId,long userId,SqlConnection connection,SqlTransaction transaction,CancellationToken ct)
    {
        var logs=new List<(long Id,string Action,string Detail,DateTime Time)>();
        const string logSql="""
            SELECT Id,Action,DetailJson,CreateTime FROM dbo.CleanupLog
            WHERE ObjectType='JOB' AND ObjectId=@JobId AND Action IN('REJECT_OVER_AGE','REJECT_OVER_SALARY')
              AND DetailJson IS NOT NULL
            ORDER BY Id
            """;
        await using(var command=new SqlCommand(logSql,connection,transaction))
        {
            command.Parameters.AddWithValue("@JobId",jobId);await using var reader=await command.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))logs.Add((reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetDateTime(3)));
        }
        if(logs.Count==0)return 0;

        var candidates=new List<(long Id,string? RawText,DateTime UpdateTime)>();
        const string candidateSql="""
            SELECT cj.Id,r.RawText,cj.UpdateTime
            FROM dbo.CandidateJob cj JOIN dbo.Candidate c ON c.Id=cj.CandidateId AND c.IsDeleted=0
            LEFT JOIN dbo.Resume r ON r.CandidateId=c.Id AND r.IsCurrent=1 AND r.IsDeleted=0
            WHERE cj.JobId=@JobId AND cj.Status='REJECTED' AND cj.ReviewedBy IS NULL
              AND NOT EXISTS(SELECT 1 FROM dbo.JobCandidateCleanupHistory h WHERE h.CandidateJobId=cj.Id)
            """;
        await using(var command=new SqlCommand(candidateSql,connection,transaction))
        {
            command.Parameters.AddWithValue("@JobId",jobId);await using var reader=await command.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))candidates.Add((reader.GetInt64(0),reader.GetNullableString(1),reader.GetDateTime(2)));
        }

        var ids=new HashSet<long>();
        foreach(var log in logs)
        {
            try
            {
                using var document=JsonDocument.Parse(log.Detail);var root=document.RootElement;
                if(!root.TryGetProperty("cleanedCount",out var countElement)||!countElement.TryGetInt32(out var expected)||expected<=0)continue;
                var from=log.Time.AddHours(-2);var to=log.Time.AddSeconds(10);
                IEnumerable<(long Id,string? RawText,DateTime UpdateTime)> matching=candidates.Where(x=>x.UpdateTime>=from&&x.UpdateTime<=to&&!ids.Contains(x.Id));
                if(log.Action=="REJECT_OVER_AGE"&&root.TryGetProperty("maxAge",out var ageElement)&&ageElement.TryGetInt32(out var maxAge))
                    matching=matching.Where(x=>ParseCandidateAge(x.RawText)>maxAge);
                else if(log.Action=="REJECT_OVER_SALARY"&&root.TryGetProperty("maxExpectedMonthlySalary",out var salaryElement)&&salaryElement.TryGetDecimal(out var maxSalary))
                    matching=matching.Where(x=>ParseExpectedMonthlySalaryUpper(x.RawText)>maxSalary);
                else continue;
                foreach(var candidate in matching.OrderBy(x=>Math.Abs((log.Time-x.UpdateTime).TotalSeconds)).Take(expected))ids.Add(candidate.Id);
            }
            catch(JsonException){ }
        }
        foreach(var candidateJobId in ids)
        {
            await using var restore=new SqlCommand("UPDATE dbo.CandidateJob SET Status='PENDING',UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND JobId=@JobId AND Status='REJECTED'",connection,transaction);
            restore.Parameters.AddWithValue("@Id",candidateJobId);restore.Parameters.AddWithValue("@JobId",jobId);await restore.ExecuteNonQueryAsync(ct);
        }
        if(ids.Count>0)
        {
            await using var audit=new SqlCommand("INSERT dbo.CleanupLog(ObjectType,ObjectId,Action,Reason,OperatorId,DetailJson) VALUES('JOB',@JobId,'RESTORE_LEGACY_CLEANUP',N'兼容恢复V019之前的一键清理记录',@UserId,@Detail)",connection,transaction);
            audit.Parameters.AddWithValue("@JobId",jobId);audit.Parameters.AddWithValue("@UserId",userId);audit.Parameters.Add("@Detail",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(new{jobId,restoredCount=ids.Count});await audit.ExecuteNonQueryAsync(ct);
        }
        return ids.Count;
    }

    public async Task UpdateScoreConfigAsync(long id, UpdateScoreConfigRequest request, long userId, CancellationToken ct)
    {
        if (new[] { request.SkillWeight, request.ProjectWeight, request.ExperienceWeight, request.EducationWeight }.Any(x => x < 0 || x > 100) ||
            request.SkillWeight + request.ProjectWeight + request.ExperienceWeight + request.EducationWeight != 100m)
            throw new BusinessException("SCORE_WEIGHT_INVALID", "四项评分权重必须在0至100之间且合计100。 ");
        if (new[] { request.DutyWeight,request.ProductWeight,request.IndustryWeight,request.TenureWeight,request.CompanyEnvironmentWeight }.Any(x=>x<0||x>100) ||
            request.DutyWeight+request.ProductWeight+request.IndustryWeight+request.TenureWeight+request.CompanyEnvironmentWeight!=100m)
            throw new BusinessException("EXPERIENCE_WEIGHT_INVALID","经验内部五项权重必须在0至100之间且合计100。");
        if (request.RuleJson is { Length: > 20000 }) throw new BusinessException("RULE_JSON_TOO_LONG", "评分规则JSON不能超过20000字。 ");
        if (request.RuleJson is not null)
        {
            try { using var document = JsonDocument.Parse(request.RuleJson); if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException(); }
            catch (JsonException) { throw new BusinessException("RULE_JSON_INVALID", "评分规则必须是JSON对象。 "); }
        }
        var version = ParseVersion(request.RowVersion);
        const string sql = """
            SET NOCOUNT ON;
            UPDATE dbo.Job SET UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND IsDeleted=0 AND RowVersion=@Version;
            IF @@ROWCOUNT=0 SELECT 0;
            ELSE BEGIN
              UPDATE dbo.ScoreConfig SET SkillWeight=@Skill,ProjectWeight=@Project,ExperienceWeight=@Experience,EducationWeight=@Education,
                DutyWeight=@Duty,ProductWeight=@Product,IndustryWeight=@Industry,TenureWeight=@Tenure,CompanyEnvironmentWeight=@CompanyEnvironment,
                RuleJson=COALESCE(@RuleJson,RuleJson),Version=Version+1,UpdatedBy=@UserId,UpdateTime=SYSUTCDATETIME() WHERE JobId=@Id;
              INSERT dbo.ScoreConfigHistory(JobId,Version,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,RuleJson,UpdatedBy,DutyWeight,ProductWeight,IndustryWeight,TenureWeight,CompanyEnvironmentWeight)
              SELECT JobId,Version,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,RuleJson,@UserId,DutyWeight,ProductWeight,IndustryWeight,TenureWeight,CompanyEnvironmentWeight FROM dbo.ScoreConfig WHERE JobId=@Id;
              SELECT @@ROWCOUNT;
            END
            """;
        await using var connection = await connectionFactory.OpenAsync(ct); await using var command = new SqlCommand(sql, connection);
        AddDecimal(command, "@Skill", request.SkillWeight); AddDecimal(command, "@Project", request.ProjectWeight); AddDecimal(command, "@Experience", request.ExperienceWeight); AddDecimal(command, "@Education", request.EducationWeight);
        AddDecimal(command,"@Duty",request.DutyWeight);AddDecimal(command,"@Product",request.ProductWeight);AddDecimal(command,"@Industry",request.IndustryWeight);AddDecimal(command,"@Tenure",request.TenureWeight);AddDecimal(command,"@CompanyEnvironment",request.CompanyEnvironmentWeight);
        AddNullable(command, "@RuleJson", SqlDbType.NVarChar, -1, request.RuleJson); command.Parameters.AddWithValue("@UserId", userId); command.Parameters.AddWithValue("@Id", id); command.Parameters.Add("@Version", SqlDbType.Timestamp).Value = version;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 1) throw new BusinessException("CONCURRENCY_CONFLICT", "岗位已被修改，请刷新。", 409);
    }

    public async Task MergeJobAsync(long sourceJobId, MergeJobRequest request, long userId, CancellationToken ct)
    {
        var targetJobId = request.TargetJobId;
        if (sourceJobId <= 0 || targetJobId <= 0 || sourceJobId == targetJobId)
            throw new BusinessException("JOB_MERGE_TARGET_INVALID", "源岗位和目标岗位必须是两个不同的有效岗位。");
        var reason = request.Reason?.Trim();
        if (reason?.Length > 500) throw new BusinessException("JOB_MERGE_REASON_TOO_LONG", "合并原因不能超过500字。");

        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            string? sourceName = null;
            string? targetName = null;
            string? targetStatus = null;
            var sourceResumeCount = 0;
            var targetResumeCount = 0;
            await using (var lockJobs = new SqlCommand("""
                SELECT j.Id,j.Name,j.Status,COUNT(DISTINCT r.Id) ResumeCount
                FROM dbo.Job j WITH (UPDLOCK,HOLDLOCK)
                LEFT JOIN dbo.CandidateJob cj ON cj.JobId=j.Id AND cj.Status<>'REJECTED'
                LEFT JOIN dbo.Resume r ON r.CandidateId=cj.CandidateId AND r.IsCurrent=1 AND r.IsDeleted=0
                WHERE j.Id IN (@SourceId,@TargetId) AND j.IsDeleted=0
                GROUP BY j.Id,j.Name,j.Status
                ORDER BY j.Id
                """, connection, transaction))
            {
                lockJobs.Parameters.AddWithValue("@SourceId", sourceJobId);
                lockJobs.Parameters.AddWithValue("@TargetId", targetJobId);
                await using var reader = await lockJobs.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    if (reader.GetInt64(0) == sourceJobId) { sourceName = reader.GetString(1); sourceResumeCount = reader.GetInt32(3); }
                    if (reader.GetInt64(0) == targetJobId) { targetName = reader.GetString(1); targetStatus = reader.GetString(2); targetResumeCount = reader.GetInt32(3); }
                }
            }
            if (sourceName is null || targetName is null)
                throw new BusinessException("JOB_MERGE_NOT_FOUND", "源岗位或目标岗位不存在。", 404);
            if (targetStatus is not ("DRAFT" or "OPEN"))
                throw new BusinessException("JOB_MERGE_TARGET_CLOSED", "目标岗位必须是草稿或开放状态。", 409);
            if (targetResumeCount <= sourceResumeCount)
                throw new BusinessException("JOB_MERGE_DIRECTION_INVALID", $"只能把关联简历较少的岗位合并到较多的岗位。源岗位 {sourceResumeCount} 份，目标岗位 {targetResumeCount} 份。", 409);

            const string mergeSql = """
                UPDATE t SET ManualScore=COALESCE(t.ManualScore,s.ManualScore),UpdateTime=SYSUTCDATETIME()
                  FROM dbo.CandidateJob t JOIN dbo.CandidateJob s ON s.CandidateId=t.CandidateId AND s.JobId=@SourceId
                 WHERE t.JobId=@TargetId;
                UPDATE t SET IsFavorite=1,
                    FavoriteTime=COALESCE(t.FavoriteTime,s.FavoriteTime),FavoriteBy=COALESCE(t.FavoriteBy,s.FavoriteBy),
                    UpdateTime=SYSUTCDATETIME()
                  FROM dbo.CandidateJob t JOIN dbo.CandidateJob s ON s.CandidateId=t.CandidateId AND s.JobId=@SourceId
                 WHERE t.JobId=@TargetId AND s.IsFavorite=1;
                UPDATE s SET JobId=@TargetId,UpdateTime=SYSUTCDATETIME()
                  FROM dbo.CandidateJob s
                 WHERE s.JobId=@SourceId AND NOT EXISTS(
                    SELECT 1 FROM dbo.CandidateJob t WHERE t.CandidateId=s.CandidateId AND t.JobId=@TargetId);
                UPDATE h SET JobId=@TargetId
                  FROM dbo.JobCandidateCleanupHistory h JOIN dbo.CandidateJob cj ON cj.Id=h.CandidateJobId
                 WHERE h.JobId=@SourceId AND cj.JobId=@TargetId;
                UPDATE dbo.ResumeJobSource SET JobId=@TargetId,UpdateTime=SYSUTCDATETIME() WHERE JobId=@SourceId;
                UPDATE dbo.ExternalJobMapping SET JobId=@TargetId,MappingStatus='CONFIRMED',UpdateTime=SYSUTCDATETIME() WHERE JobId=@SourceId;
                UPDATE dbo.ResumeImportFile SET SourceJobId=@TargetId,UpdateTime=SYSUTCDATETIME() WHERE SourceJobId=@SourceId;
                DELETE s FROM dbo.ResumeImportBatchJob s WHERE s.JobId=@SourceId AND EXISTS(
                    SELECT 1 FROM dbo.ResumeImportBatchJob t WHERE t.BatchId=s.BatchId AND t.JobId=@TargetId);
                UPDATE dbo.ResumeImportBatchJob SET JobId=@TargetId WHERE JobId=@SourceId;
                DELETE s FROM dbo.JobKnowledgeDocument s WHERE s.JobId=@SourceId AND EXISTS(
                    SELECT 1 FROM dbo.JobKnowledgeDocument t WHERE t.JobId=@TargetId AND t.KnowledgeDocumentId=s.KnowledgeDocumentId);
                IF EXISTS(SELECT 1 FROM dbo.JobKnowledgeDocument WHERE JobId=@TargetId AND IsPrimary=1 AND RelationType='PRIMARY_REQUIREMENT')
                  UPDATE dbo.JobKnowledgeDocument SET RelationType='SUPPLEMENT',IsPrimary=0,Priority=50 WHERE JobId=@SourceId AND IsPrimary=1 AND RelationType='PRIMARY_REQUIREMENT';
                UPDATE dbo.JobKnowledgeDocument SET JobId=@TargetId WHERE JobId=@SourceId;
                UPDATE dbo.Job SET IsDeleted=1,Status='ARCHIVED',UpdateTime=SYSUTCDATETIME() WHERE Id=@SourceId;
                UPDATE dbo.Job SET UpdateTime=SYSUTCDATETIME() WHERE Id=@TargetId;
                """;
            await using (var merge = new SqlCommand(mergeSql, connection, transaction))
            {
                merge.Parameters.AddWithValue("@SourceId", sourceJobId);
                merge.Parameters.AddWithValue("@TargetId", targetJobId);
                await merge.ExecuteNonQueryAsync(ct);
            }
            await using (var audit = new SqlCommand("INSERT dbo.CleanupLog(ObjectType,ObjectId,Action,Reason,OperatorId,DetailJson) VALUES('JOB',@SourceId,'MERGE_JOB',@Reason,@UserId,@Detail)", connection, transaction))
            {
                audit.Parameters.AddWithValue("@SourceId", sourceJobId);
                audit.Parameters.AddWithValue("@UserId", userId);
                audit.Parameters.Add("@Reason", SqlDbType.NVarChar, 500).Value = (object?)reason ?? DBNull.Value;
                audit.Parameters.Add("@Detail", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(new { sourceJobId, sourceName, sourceResumeCount, targetJobId, targetName, targetResumeCount });
                await audit.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<QueueScoreRunResult> QueueJobScoringAsync(long id,long userId,CancellationToken ct)
    {
        const string sql="""
          SET NOCOUNT ON;
          IF NOT EXISTS(SELECT 1 FROM dbo.Job WHERE Id=@Id AND IsDeleted=0) SELECT CAST(-1 AS BIGINT),0,0;
          ELSE IF NOT EXISTS(SELECT 1 FROM dbo.JobKnowledgeDocument x JOIN dbo.KnowledgeDocument d ON d.Id=x.KnowledgeDocumentId
            WHERE x.JobId=@Id AND x.IsPrimary=1 AND x.RelationType='PRIMARY_REQUIREMENT' AND d.IsDeleted=0) SELECT CAST(-2 AS BIGINT),0,0;
          ELSE BEGIN
            DECLARE @RunNo INT,@RunId BIGINT,@Queued INT;
            SELECT @RunNo=ISNULL(MAX(RunNo),0)+1 FROM dbo.ScoringRun WITH(UPDLOCK,HOLDLOCK) WHERE JobId=@Id;
            INSERT dbo.ScoringRun(JobId,RunNo,ScoreConfigVersion,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,
              DutyWeight,ProductWeight,IndustryWeight,TenureWeight,CompanyEnvironmentWeight,RuleJson,
              JobName,JobDescription,JobRequirement,SkillRequirement,EducationRequirement,MinExperienceMonths,
              JobProfileDocumentId,JobProfileVersion,JobProfileContent,CreatedBy)
            SELECT j.Id,@RunNo,s.Version,s.SkillWeight,s.ProjectWeight,s.ExperienceWeight,s.EducationWeight,
              s.DutyWeight,s.ProductWeight,s.IndustryWeight,s.TenureWeight,s.CompanyEnvironmentWeight,s.RuleJson,
              j.Name,j.Description,j.Requirement,j.SkillRequirement,j.EducationRequirement,j.MinExperienceMonths,
              d.Id,d.Version,d.Content,@UserId
            FROM dbo.Job j JOIN dbo.ScoreConfig s ON s.JobId=j.Id
            CROSS APPLY(SELECT TOP(1) d.Id,d.Version,d.Content
              FROM dbo.JobKnowledgeDocument x JOIN dbo.KnowledgeDocument d ON d.Id=x.KnowledgeDocumentId
              WHERE x.JobId=j.Id AND x.IsPrimary=1 AND x.RelationType='PRIMARY_REQUIREMENT' AND d.IsDeleted=0
              ORDER BY CASE WHEN d.Status='ACTIVE' THEN 0 ELSE 1 END,d.Version DESC,d.Id DESC) d
            WHERE j.Id=@Id AND j.IsDeleted=0;
            SET @RunId=SCOPE_IDENTITY();
            INSERT dbo.ScoringRunResult(ScoringRunId,CandidateJobId,CandidateId,ResumeId,CandidateName,CurrentCompany,ResumeImportTime,Status)
            SELECT @RunId,cj.Id,c.Id,r.Id,c.Name,cp.CurrentCompany,r.CreateTime,'WAITING'
            FROM dbo.CandidateJob cj JOIN dbo.Candidate c ON c.Id=cj.CandidateId
            JOIN dbo.Resume r ON r.CandidateId=c.Id AND r.IsCurrent=1 AND r.IsDeleted=0 AND r.IsAiAnalyzed=1
            LEFT JOIN dbo.CandidateProfile cp ON cp.CandidateId=c.Id
            WHERE cj.JobId=@Id AND cj.Status<>'REJECTED' AND c.IsDeleted=0;
            SET @Queued=@@ROWCOUNT;
            IF @Queued=0 BEGIN DELETE dbo.ScoringRun WHERE Id=@RunId; SELECT CAST(-3 AS BIGINT),0,0; END
            ELSE BEGIN
              INSERT dbo.AITask(TaskType,CandidateJobId,ScoringRunId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey)
              SELECT 'MATCH_SCORE',r.CandidateJobId,@RunId,'WAITING',3,0,1,
                'match-score:'+CONVERT(VARCHAR(20),@RunId)+':'+CONVERT(VARCHAR(20),r.CandidateJobId)
              FROM dbo.ScoringRunResult r WHERE r.ScoringRunId=@RunId;
              SELECT @RunId,@RunNo,@Queued;
            END
          END
          """;
        await using var connection=await connectionFactory.OpenAsync(ct);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            await using var command=new SqlCommand(sql,connection,transaction);command.Parameters.AddWithValue("@Id",id);command.Parameters.AddWithValue("@UserId",userId);
            await using var reader=await command.ExecuteReaderAsync(ct);await reader.ReadAsync(ct);
            var runId=reader.GetInt64(0);var runNo=reader.GetInt32(1);var queued=reader.GetInt32(2);
            if(runId==-1)throw new BusinessException("JOB_NOT_FOUND","岗位不存在。",404);
            if(runId==-2)throw new BusinessException("JOB_PROFILE_MISSING","请先完善岗位画像，再启动岗位评分。",409);
            if(runId==-3)throw new BusinessException("NO_SCORABLE_CANDIDATES","当前岗位没有已完成AI简历分析的候选人。",409);
            await reader.DisposeAsync();await transaction.CommitAsync(ct);return new(runId,runNo,queued);
        }
        catch{await transaction.RollbackAsync(ct);throw;}
    }

    public async Task<JobProfileDto?> GetJobProfileAsync(long jobId,CancellationToken ct)
    {
        const string sql="""
          SELECT x.JobId,d.Id,d.Title,d.Content,d.Status,d.Version,d.RowVersion,d.UpdateTime
          FROM dbo.JobKnowledgeDocument x JOIN dbo.KnowledgeDocument d ON d.Id=x.KnowledgeDocumentId
          JOIN dbo.Job j ON j.Id=x.JobId
          WHERE x.JobId=@JobId AND x.IsPrimary=1 AND x.RelationType='PRIMARY_REQUIREMENT' AND d.IsDeleted=0 AND j.IsDeleted=0
          """;
        await using var connection=await connectionFactory.OpenAsync(ct);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@JobId",jobId);
        await using var reader=await command.ExecuteReaderAsync(ct);return await reader.ReadAsync(ct)?new(reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetInt32(5),Convert.ToBase64String((byte[])reader[6]),reader.GetDateTime(7)):null;
    }

    public async Task UpdateJobProfileAsync(long jobId,SaveJobProfileRequest request,CancellationToken ct)
    {
        var title=request.Title.Trim();var content=request.Content.Trim();
        if(title.Length is <1 or >300||content.Length is <1 or >50000)throw new BusinessException("JOB_PROFILE_INVALID","岗位画像标题必须为1至300字，内容必须为1至50000字。");
        var version=ParseVersion(request.RowVersion);
        const string sql="""
          UPDATE d SET Title=@Title,Content=@Content,ContentHash=@Hash,Status='DRAFT',Version=Version+1,UpdateTime=SYSUTCDATETIME()
          FROM dbo.KnowledgeDocument d JOIN dbo.JobKnowledgeDocument x ON x.KnowledgeDocumentId=d.Id
          WHERE x.JobId=@JobId AND x.IsPrimary=1 AND x.RelationType='PRIMARY_REQUIREMENT' AND d.IsDeleted=0 AND d.RowVersion=@Version;
          DECLARE @Updated INT=@@ROWCOUNT;
          SELECT @Updated;
          """;
        await using var connection=await connectionFactory.OpenAsync(ct);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@JobId",jobId);command.Parameters.Add("@Title",SqlDbType.NVarChar,300).Value=title;command.Parameters.Add("@Content",SqlDbType.NVarChar,-1).Value=content;command.Parameters.Add("@Hash",SqlDbType.Char,64).Value=HashContent(content);command.Parameters.Add("@Version",SqlDbType.Timestamp).Value=version;
        if(Convert.ToInt32(await command.ExecuteScalarAsync(ct))!=1)throw new BusinessException("CONCURRENCY_CONFLICT","岗位画像已变化，请刷新后重试。",409);
    }

    public async Task<IReadOnlyList<ScoreRunDto>> ListScoreRunsAsync(long id,CancellationToken ct)
    {
        const string sql="""
          SELECT sr.Id,sr.RunNo,sr.ScoreConfigVersion,sr.SkillWeight,sr.ProjectWeight,sr.ExperienceWeight,sr.EducationWeight,
            sr.DutyWeight,sr.ProductWeight,sr.IndustryWeight,sr.TenureWeight,sr.CompanyEnvironmentWeight,
            SUM(CASE WHEN r.Status='WAITING' THEN 1 ELSE 0 END),SUM(CASE WHEN r.Status='RUNNING' THEN 1 ELSE 0 END),
            SUM(CASE WHEN r.Status='SUCCESS' THEN 1 ELSE 0 END),SUM(CASE WHEN r.Status='FAILED' THEN 1 ELSE 0 END),
            SUM(CASE WHEN r.Status='CANCELLED' THEN 1 ELSE 0 END),sr.CreateTime
          FROM dbo.ScoringRun sr LEFT JOIN dbo.ScoringRunResult r ON r.ScoringRunId=sr.Id
          WHERE sr.JobId=@Id
          GROUP BY sr.Id,sr.RunNo,sr.ScoreConfigVersion,sr.SkillWeight,sr.ProjectWeight,sr.ExperienceWeight,sr.EducationWeight,
            sr.DutyWeight,sr.ProductWeight,sr.IndustryWeight,sr.TenureWeight,sr.CompanyEnvironmentWeight,sr.CreateTime
          ORDER BY sr.RunNo DESC,sr.Id DESC;
          """;
        await using var connection=await connectionFactory.OpenAsync(ct);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@Id",id);
        await using var reader=await command.ExecuteReaderAsync(ct);var items=new List<ScoreRunDto>();while(await reader.ReadAsync(ct))items.Add(MapScoreRun(reader));return items;
    }

    public async Task<JobRankingPage> GetJobRankingAsync(long id,long? runId,int page,int pageSize,DateOnly? resumeFrom,DateOnly? resumeTo,string? talentId,CancellationToken ct)
    {
        page=Math.Max(1,page);pageSize=Math.Clamp(pageSize,1,100);
        talentId=string.IsNullOrWhiteSpace(talentId)?null:talentId.Trim();
        if(talentId is not null&&(talentId.Length>100||talentId.Any(c=>!char.IsDigit(c))))
            throw new BusinessException("INVALID_TALENT_ID","51JOB人才ID必须是数字，且不能超过100位。");
        if(resumeFrom.HasValue&&resumeTo.HasValue&&resumeFrom.Value>resumeTo.Value)
            throw new BusinessException("INVALID_RESUME_IMPORT_DATE_RANGE","简历导入开始日期不能晚于结束日期。");
        if(resumeTo==DateOnly.MaxValue)
            throw new BusinessException("INVALID_RESUME_IMPORT_DATE_RANGE","简历导入结束日期超出支持范围。");
        DateTime? fromUtc=resumeFrom.HasValue?ChinaDateBoundaryToUtc(resumeFrom.Value):null;
        DateTime? toUtcExclusive=resumeTo.HasValue?ChinaDateBoundaryToUtc(resumeTo.Value.AddDays(1)):null;
        const string sql="""
          DECLARE @SelectedRunId BIGINT;
          SET @SelectedRunId=COALESCE(@RunId,(SELECT TOP(1) Id FROM dbo.ScoringRun WHERE JobId=@Id ORDER BY RunNo DESC,Id DESC));
          SELECT sr.Id,sr.RunNo,sr.ScoreConfigVersion,sr.SkillWeight,sr.ProjectWeight,sr.ExperienceWeight,sr.EducationWeight,
            sr.DutyWeight,sr.ProductWeight,sr.IndustryWeight,sr.TenureWeight,sr.CompanyEnvironmentWeight,
            SUM(CASE WHEN r.Status='WAITING' THEN 1 ELSE 0 END),SUM(CASE WHEN r.Status='RUNNING' THEN 1 ELSE 0 END),
            SUM(CASE WHEN r.Status='SUCCESS' THEN 1 ELSE 0 END),SUM(CASE WHEN r.Status='FAILED' THEN 1 ELSE 0 END),
            SUM(CASE WHEN r.Status='CANCELLED' THEN 1 ELSE 0 END),sr.CreateTime
          FROM dbo.ScoringRun sr LEFT JOIN dbo.ScoringRunResult r ON r.ScoringRunId=sr.Id
            AND (@ResumeFromUtc IS NULL OR r.ResumeImportTime>=@ResumeFromUtc)
            AND (@ResumeToUtc IS NULL OR r.ResumeImportTime<@ResumeToUtc)
            AND (@TalentId IS NULL OR EXISTS(SELECT 1 FROM dbo.Resume tr WHERE tr.Id=r.ResumeId AND tr.TalentId=@TalentId))
          WHERE sr.Id=@SelectedRunId AND sr.JobId=@Id
          GROUP BY sr.Id,sr.RunNo,sr.ScoreConfigVersion,sr.SkillWeight,sr.ProjectWeight,sr.ExperienceWeight,sr.EducationWeight,
            sr.DutyWeight,sr.ProductWeight,sr.IndustryWeight,sr.TenureWeight,sr.CompanyEnvironmentWeight,sr.CreateTime;
          SELECT COUNT(*) FROM dbo.ScoringRunResult rr WHERE rr.ScoringRunId=@SelectedRunId AND rr.Status='SUCCESS'
            AND (@ResumeFromUtc IS NULL OR rr.ResumeImportTime>=@ResumeFromUtc)
            AND (@ResumeToUtc IS NULL OR rr.ResumeImportTime<@ResumeToUtc)
            AND (@TalentId IS NULL OR EXISTS(SELECT 1 FROM dbo.Resume tr WHERE tr.Id=rr.ResumeId AND tr.TalentId=@TalentId));
          WITH P AS(SELECT r.CandidateJobId,r.CandidateId,r.CandidateName,r.AIScore,r.SkillScore,r.ProjectScore,r.ExperienceScore,r.EducationScore,
            r.CurrentCompany,r.Recommendation,sr.ScoreConfigVersion,r.UpdateTime,CONVERT(BIT,COALESCE(cj.IsFavorite,0)) IsFavorite,r.ResumeImportTime,
            ROW_NUMBER() OVER(ORDER BY r.AIScore DESC,r.Id) rn
            FROM dbo.ScoringRunResult r JOIN dbo.ScoringRun sr ON sr.Id=r.ScoringRunId
            LEFT JOIN dbo.CandidateJob cj ON cj.Id=r.CandidateJobId
            WHERE r.ScoringRunId=@SelectedRunId AND sr.JobId=@Id AND r.Status='SUCCESS'
              AND (@ResumeFromUtc IS NULL OR r.ResumeImportTime>=@ResumeFromUtc)
              AND (@ResumeToUtc IS NULL OR r.ResumeImportTime<@ResumeToUtc)
              AND (@TalentId IS NULL OR EXISTS(SELECT 1 FROM dbo.Resume tr WHERE tr.Id=r.ResumeId AND tr.TalentId=@TalentId)))
          SELECT CandidateJobId,CandidateId,CandidateName,AIScore,SkillScore,ProjectScore,ExperienceScore,EducationScore,CurrentCompany,Recommendation,ScoreConfigVersion,UpdateTime,IsFavorite,ResumeImportTime
          FROM P WHERE rn BETWEEN @Start AND @End ORDER BY rn;
          """;
        await using var connection=await connectionFactory.OpenAsync(ct);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@Id",id);command.Parameters.AddWithValue("@RunId",(object?)runId??DBNull.Value);command.Parameters.AddWithValue("@Start",(page-1)*pageSize+1);command.Parameters.AddWithValue("@End",page*pageSize);command.Parameters.Add("@ResumeFromUtc",SqlDbType.DateTime2).Value=(object?)fromUtc??DBNull.Value;command.Parameters.Add("@ResumeToUtc",SqlDbType.DateTime2).Value=(object?)toUtcExclusive??DBNull.Value;command.Parameters.Add("@TalentId",SqlDbType.NVarChar,100).Value=(object?)talentId??DBNull.Value;
        await using var reader=await command.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct))return new(Array.Empty<JobRankingItemDto>(),0,page,pageSize,0,0,0,null);
        var run=MapScoreRun(reader);
        await reader.NextResultAsync(ct);await reader.ReadAsync(ct);var total=reader.GetInt32(0);
        await reader.NextResultAsync(ct);var items=new List<JobRankingItemDto>();while(await reader.ReadAsync(ct))items.Add(new(reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),reader.GetNullableDecimal(3),reader.GetNullableDecimal(4),reader.GetNullableDecimal(5),reader.GetNullableDecimal(6),reader.GetNullableDecimal(7),reader.GetNullableString(8),reader.GetNullableString(9),reader.GetNullableInt32(10),reader.GetDateTime(11),reader.GetBoolean(12),reader.GetDateTime(13)));
        return new(items,total,page,pageSize,run.WaitingCount,run.RunningCount,run.FailedCount,run);
    }

    private static DateTime ChinaDateBoundaryToUtc(DateOnly date)
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows()?"China Standard Time":"Asia/Shanghai");
        return TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue,DateTimeKind.Unspecified),zone);
    }

    public async Task<IReadOnlyList<JobMergeTargetDto>> GetJobMergeTargetsAsync(long sourceJobId,CancellationToken ct)
    {
        const string sql="""
          SELECT j.Id,j.Name,j.Status,COUNT(DISTINCT r.Id) ResumeCount
          FROM dbo.Job j
          LEFT JOIN dbo.CandidateJob cj ON cj.JobId=j.Id AND cj.Status<>'REJECTED'
          LEFT JOIN dbo.Resume r ON r.CandidateId=cj.CandidateId AND r.IsCurrent=1 AND r.IsDeleted=0
          WHERE j.IsDeleted=0
          GROUP BY j.Id,j.Name,j.Status
          ORDER BY COUNT(DISTINCT r.Id) DESC,j.Name,j.Id;
          """;
        await using var connection=await connectionFactory.OpenAsync(ct);await using var command=new SqlCommand(sql,connection);
        await using var reader=await command.ExecuteReaderAsync(ct);var rows=new List<(long Id,string Name,string Status,int Count)>();
        while(await reader.ReadAsync(ct))rows.Add((reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetInt32(3)));
        var source=rows.FirstOrDefault(x=>x.Id==sourceJobId);
        if(source.Id==0)throw new BusinessException("JOB_NOT_FOUND","岗位不存在。",404);
        return rows.Where(x=>x.Id!=sourceJobId).Select(x=>
        {
            string? reason=null;
            if(x.Status is not ("DRAFT" or "OPEN"))reason="已关闭，不能作为目标";
            else if(x.Count<source.Count)reason="关联简历数少于当前岗位";
            else if(x.Count==source.Count)reason="关联简历数与当前岗位相同";
            return new JobMergeTargetDto(x.Id,x.Name,x.Status,x.Count,reason is null,reason);
        }).ToList();
    }

    public async Task SetCandidateJobFavoriteAsync(long candidateJobId,bool favorite,long userId,CancellationToken ct)
    {
        const string sql="""
          UPDATE cj SET IsFavorite=@Favorite,
            FavoriteTime=CASE WHEN @Favorite=1 THEN COALESCE(FavoriteTime,SYSUTCDATETIME()) ELSE NULL END,
            FavoriteBy=CASE WHEN @Favorite=1 THEN @UserId ELSE NULL END,UpdateTime=SYSUTCDATETIME()
          FROM dbo.CandidateJob cj JOIN dbo.Candidate c ON c.Id=cj.CandidateId JOIN dbo.Job j ON j.Id=cj.JobId
          WHERE cj.Id=@Id AND c.IsDeleted=0 AND j.IsDeleted=0;
          """;
        await using var connection=await connectionFactory.OpenAsync(ct);await using var command=new SqlCommand(sql,connection);
        command.Parameters.AddWithValue("@Id",candidateJobId);command.Parameters.AddWithValue("@Favorite",favorite);command.Parameters.AddWithValue("@UserId",userId);
        if(await command.ExecuteNonQueryAsync(ct)!=1)throw new BusinessException("CANDIDATE_JOB_NOT_FOUND","候选人与岗位关系不存在。",404);
    }

    public async Task<FavoriteCandidateJobPage> ListFavoriteCandidateJobsAsync(int page,int pageSize,long? jobId,CancellationToken ct)
    {
        page=Math.Max(1,page);pageSize=Math.Clamp(pageSize,1,100);
        const string sql="""
          SELECT COUNT(*) FROM dbo.CandidateJob cj JOIN dbo.Candidate c ON c.Id=cj.CandidateId JOIN dbo.Job j ON j.Id=cj.JobId
           WHERE cj.IsFavorite=1 AND cj.AIScore IS NOT NULL AND c.IsDeleted=0 AND j.IsDeleted=0
             AND (@JobId IS NULL OR cj.JobId=@JobId);
          ;WITH Ranked AS(
            SELECT cj.Id CandidateJobId,cj.CandidateId,c.Name CandidateName,cj.JobId,j.Name JobName,
              ROW_NUMBER() OVER(PARTITION BY cj.JobId ORDER BY cj.AIScore DESC,cj.Id) JobRank,
              cj.AIScore,cj.SkillScore,cj.ProjectScore,cj.ExperienceScore,cj.EducationScore,
              cp.CurrentCompany,cj.Recommendation,cj.ScoreConfigVersion,
              ISNULL(cj.FavoriteTime,cj.UpdateTime) FavoriteTime,cj.IsFavorite,
              CASE WHEN c.PhoneNormalized IS NOT NULL AND LEN(c.PhoneNormalized)>0 THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END HasPhone,
              CASE WHEN EXISTS(SELECT 1 FROM dbo.InterviewQuestionSet qs WHERE qs.CandidateJobId=cj.Id AND qs.Status IN('GENERATING','DRAFT','CONFIRMED')) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END HasActiveInterview,
              (SELECT TOP 1 qs.Id FROM dbo.InterviewQuestionSet qs WHERE qs.CandidateJobId=cj.Id AND qs.Status IN('GENERATING','DRAFT','CONFIRMED') ORDER BY qs.Id DESC) ActiveQuestionSetId
            FROM dbo.CandidateJob cj JOIN dbo.Candidate c ON c.Id=cj.CandidateId JOIN dbo.Job j ON j.Id=cj.JobId
            LEFT JOIN dbo.CandidateProfile cp ON cp.CandidateId=c.Id
            WHERE c.IsDeleted=0 AND j.IsDeleted=0 AND cj.AIScore IS NOT NULL
              AND (@JobId IS NULL OR cj.JobId=@JobId)),
          Favorites AS(
            SELECT *,ROW_NUMBER() OVER(ORDER BY FavoriteTime DESC,CandidateJobId DESC) PageRow FROM Ranked WHERE IsFavorite=1)
          SELECT CandidateJobId,CandidateId,CandidateName,JobId,JobName,JobRank,AIScore,SkillScore,ProjectScore,ExperienceScore,EducationScore,
            CurrentCompany,Recommendation,ScoreConfigVersion,FavoriteTime,HasPhone,HasActiveInterview,ActiveQuestionSetId
          FROM Favorites WHERE PageRow BETWEEN @Start AND @End ORDER BY PageRow;
          """;
        await using var connection=await connectionFactory.OpenAsync(ct);await using var command=new SqlCommand(sql,connection);
        command.Parameters.AddWithValue("@JobId",(object?)jobId??DBNull.Value);
        command.Parameters.AddWithValue("@Start",(page-1)*pageSize+1);command.Parameters.AddWithValue("@End",page*pageSize);
        await using var reader=await command.ExecuteReaderAsync(ct);await reader.ReadAsync(ct);var total=reader.GetInt32(0);
        await reader.NextResultAsync(ct);var items=new List<FavoriteCandidateJobDto>();
        while(await reader.ReadAsync(ct))items.Add(new(reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),reader.GetInt64(3),reader.GetString(4),Convert.ToInt32(reader.GetValue(5)),reader.GetNullableDecimal(6),reader.GetNullableDecimal(7),reader.GetNullableDecimal(8),reader.GetNullableDecimal(9),reader.GetNullableDecimal(10),reader.GetNullableString(11),reader.GetNullableString(12),reader.GetNullableInt32(13),reader.GetDateTime(14),reader.GetBoolean(15),reader.GetBoolean(16),reader.GetNullableInt64(17)));
        return new(items,total,page,pageSize);
    }

    public async Task<CandidatePage> ListCandidatesAsync(int page, int pageSize, string? keyword, string? status, CancellationToken ct)
    {
        page=Math.Max(1,page);pageSize=Math.Clamp(pageSize,1,100);keyword=keyword?.Trim();status=status?.Trim().ToUpperInvariant();
        // Keep the leading space: this condition is concatenated immediately after
        // the raw SQL string's WHERE token (raw strings trim the closing newline).
        const string where=" c.IsDeleted=0 AND (@Status IS NULL OR c.Status=@Status) AND (@Keyword IS NULL OR c.Name LIKE N'%'+@Keyword+N'%' OR c.Phone LIKE N'%'+@Keyword+N'%' OR c.Email LIKE N'%'+@Keyword+N'%')";
        var sql=string.Concat("""
          SET ANSI_NULLS ON;
          SET QUOTED_IDENTIFIER ON;
          SET ANSI_PADDING ON;
          SET ANSI_WARNINGS ON;
          SET ARITHABORT ON;
          SET CONCAT_NULL_YIELDS_NULL ON;
          SET NUMERIC_ROUNDABORT OFF;
          ;WITH Paged AS(SELECT c.Id,c.Name,c.Phone,c.Email,c.Source,c.Status,p.HighestEducation,p.TotalExperienceMonths,r.Id ResumeId,c.UpdateTime,c.RowVersion,
            STUFF((
              SELECT N'、'+job.Name
              FROM (
                SELECT DISTINCT j.Name
                FROM dbo.CandidateJob cj
                JOIN dbo.Job j ON j.Id=cj.JobId
                WHERE cj.CandidateId=c.Id AND cj.Status<>'REJECTED' AND j.IsDeleted=0
              ) job
              ORDER BY job.Name
              FOR XML PATH(''),TYPE
            ).value('.','NVARCHAR(MAX)'),1,1,N'') MatchedJobs,
            ROW_NUMBER() OVER(ORDER BY c.UpdateTime DESC,c.Id DESC) RowNo
            FROM dbo.Candidate c LEFT JOIN dbo.Resume r ON r.CandidateId=c.Id AND r.IsCurrent=1 AND r.IsDeleted=0
            LEFT JOIN dbo.CandidateProfile p ON p.CandidateId=c.Id
            WHERE
          """,where,"""
          )
          SELECT Id,Name,Phone,Email,Source,Status,HighestEducation,TotalExperienceMonths,ResumeId,UpdateTime,RowVersion,MatchedJobs FROM Paged WHERE RowNo BETWEEN @Start AND @End ORDER BY RowNo
          """);
        await using var connection=await connectionFactory.OpenAsync(ct);
        await using var countCommand=new SqlCommand("SELECT COUNT(*) FROM dbo.Candidate c WHERE "+where,connection);AddNullable(countCommand,"@Status",SqlDbType.VarChar,20,status);AddNullable(countCommand,"@Keyword",SqlDbType.NVarChar,200,keyword);var total=Convert.ToInt32(await countCommand.ExecuteScalarAsync(ct));
        await using var command=new SqlCommand(sql,connection);AddNullable(command,"@Status",SqlDbType.VarChar,20,status);AddNullable(command,"@Keyword",SqlDbType.NVarChar,200,keyword);command.Parameters.AddWithValue("@Start",(page-1)*pageSize+1);command.Parameters.AddWithValue("@End",page*pageSize);
        await using var reader=await command.ExecuteReaderAsync(ct);var result=new List<CandidateListItemDto>();while(await reader.ReadAsync(ct))result.Add(MapCandidate(reader));return new(result,total,page,pageSize);
    }

    public async Task<CandidateDetailDto?> GetCandidateAsync(long id,CancellationToken ct)
    {
        const string sql="""SELECT c.Id,c.Name,c.Phone,c.Email,c.Source,c.Status,p.HighestEducation,p.TotalExperienceMonths,r.Id,c.UpdateTime,c.RowVersion,p.Summary,p.CurrentCompany,p.CurrentTitle,r.TalentId,r.ResumeSource,r.RawText FROM dbo.Candidate c LEFT JOIN dbo.Resume r ON r.CandidateId=c.Id AND r.IsCurrent=1 AND r.IsDeleted=0 LEFT JOIN dbo.CandidateProfile p ON p.CandidateId=c.Id AND p.ResumeId=r.Id WHERE c.Id=@Id AND c.IsDeleted=0""";
        await using var connection=await connectionFactory.OpenAsync(ct);CandidateListItemDto? candidate;string? summary;string? company;string? title;string? talentId;string? resumeSource;string? rawText;
        await using(var command=new SqlCommand(sql,connection)){command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))return null;candidate=MapCandidate(reader);summary=reader.GetNullableString(11);company=reader.GetNullableString(12);title=reader.GetNullableString(13);talentId=reader.GetNullableString(14);resumeSource=reader.GetNullableString(15);rawText=reader.GetNullableString(16);}
        var skills=new List<string>();await using(var command=new SqlCommand("SELECT SkillName FROM dbo.CandidateSkill WHERE CandidateId=@Id ORDER BY SkillName",connection)){command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))skills.Add(reader.GetString(0));}
        var educations=new List<CandidateEducationDto>();await using(var command=new SqlCommand("SELECT SchoolName,Major,Degree,StartDate,EndDate,Description FROM dbo.CandidateEducation WHERE CandidateId=@Id ORDER BY EndDate DESC,Id DESC",connection)){command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))educations.Add(new(reader.GetString(0),reader.GetNullableString(1),reader.GetNullableString(2),reader.GetNullableDateTime(3),reader.GetNullableDateTime(4),reader.GetNullableString(5)));}
        var workExperiences=new List<CandidateWorkExperienceDto>();await using(var command=new SqlCommand("SELECT CompanyName,JobTitle,StartDate,EndDate,Description,IsCurrent FROM dbo.CandidateWorkExperience WHERE CandidateId=@Id ORDER BY IsCurrent DESC,EndDate DESC,Id DESC",connection)){command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))workExperiences.Add(new(reader.GetString(0),reader.GetNullableString(1),reader.GetNullableDateTime(2),reader.GetNullableDateTime(3),reader.GetNullableString(4),reader.GetBoolean(5)));}
        var projects=new List<CandidateProjectDto>();await using(var command=new SqlCommand("SELECT ProjectName,RoleName,Technology,StartDate,EndDate,Description FROM dbo.CandidateProject WHERE CandidateId=@Id ORDER BY EndDate DESC,Id DESC",connection)){command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))projects.Add(new(reader.GetString(0),reader.GetNullableString(1),reader.GetNullableString(2),reader.GetNullableDateTime(3),reader.GetNullableDateTime(4),reader.GetNullableString(5)));}
        var resumes=new List<ResumeVersionDto>();await using(var command=new SqlCommand("SELECT Id,FileName,FileExtension,FileSize,ParseStatus,IsCurrent,IsOptimized,CreateTime FROM dbo.Resume WHERE CandidateId=@Id AND IsDeleted=0 ORDER BY IsCurrent DESC,CreateTime DESC",connection)){command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))resumes.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetInt64(3),reader.GetString(4),reader.GetBoolean(5),reader.GetBoolean(6),reader.GetDateTime(7)));}
        return new(candidate,summary,company,title,talentId,resumeSource,rawText,skills,educations,workExperiences,projects,resumes);
    }

    public async Task UpdateCandidateAsync(long id,UpdateCandidateRequest request,CancellationToken ct)
    {
        var name=request.Name.Trim();if(name.Length is <1 or >100)throw new BusinessException("CANDIDATE_NAME_INVALID","姓名必须为1至100字。 ");var rawPhone=NormalizeOptionalText(request.Phone);var phone=NormalizePhone(rawPhone);var email=NormalizeEmail(request.Email);if(email?.Length>254)throw new BusinessException("CANDIDATE_EMAIL_INVALID","邮箱不能超过254字。 ");var version=ParseVersion(request.RowVersion);
        const string sql="UPDATE dbo.Candidate SET Name=@Name,Phone=@Phone,PhoneNormalized=@PhoneNormalized,Email=@Email,EmailNormalized=@EmailNormalized,UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND IsDeleted=0 AND RowVersion=@Version";
        await using var connection=await connectionFactory.OpenAsync(ct);
        const string duplicateSql="""
          SELECT TOP 1 Id,Name,
            CASE WHEN @Phone IS NOT NULL AND PhoneNormalized=@Phone THEN 1 ELSE 0 END PhoneDuplicated,
            CASE WHEN @Email IS NOT NULL AND EmailNormalized=@Email THEN 1 ELSE 0 END EmailDuplicated
          FROM dbo.Candidate
          WHERE Id<>@Id AND ((@Phone IS NOT NULL AND PhoneNormalized=@Phone) OR (@Email IS NOT NULL AND EmailNormalized=@Email))
          ORDER BY IsDeleted,Id;
          """;
        await using(var duplicate=new SqlCommand(duplicateSql,connection)){duplicate.Parameters.AddWithValue("@Id",id);AddNullable(duplicate,"@Phone",SqlDbType.VarChar,30,phone);AddNullable(duplicate,"@Email",SqlDbType.VarChar,254,email);await using var reader=await duplicate.ExecuteReaderAsync(ct);if(await reader.ReadAsync(ct)){var duplicateId=reader.GetInt64(0);var duplicateName=reader.GetString(1);var fields=new List<string>();if(reader.GetInt32(2)==1)fields.Add("手机号");if(reader.GetInt32(3)==1)fields.Add("邮箱");throw new BusinessException("CANDIDATE_CONTACT_EXISTS",$"{string.Join("和",fields)}已被候选人“{duplicateName}”（#{duplicateId}）使用，请核对后再保存。",409);}}
        await using var command=new SqlCommand(sql,connection);command.Parameters.Add("@Name",SqlDbType.NVarChar,100).Value=name;AddNullable(command,"@Phone",SqlDbType.NVarChar,30,rawPhone);AddNullable(command,"@PhoneNormalized",SqlDbType.VarChar,30,phone);AddNullable(command,"@Email",SqlDbType.NVarChar,254,email);AddNullable(command,"@EmailNormalized",SqlDbType.VarChar,254,email);command.Parameters.AddWithValue("@Id",id);command.Parameters.Add("@Version",SqlDbType.Timestamp).Value=version;
        try{if(await command.ExecuteNonQueryAsync(ct)!=1)throw new BusinessException("CONCURRENCY_CONFLICT","候选人已被修改，请刷新。",409);}catch(SqlException ex)when(ex.Number is 2601 or 2627){throw new BusinessException("CANDIDATE_CONTACT_EXISTS","手机号或邮箱刚被其他候选人使用，请刷新后核对。",409);}
    }

    public async Task<ResumeDownloadInfo?> GetResumeDownloadAsync(long resumeId,CancellationToken ct)
    {
        await using var connection=await connectionFactory.OpenAsync(ct);await using var command=new SqlCommand("SELECT FileName,FileExtension,FilePath FROM dbo.Resume WHERE Id=@Id AND IsDeleted=0",connection);command.Parameters.AddWithValue("@Id",resumeId);await using var reader=await command.ExecuteReaderAsync(ct);return await reader.ReadAsync(ct)?new(reader.GetString(0),reader.GetString(1),reader.GetString(2)):null;
    }

    private static JobDto MapJob(SqlDataReader r)=>new(r.GetInt64(0),r.GetString(1),r.GetNullableString(2),r.GetString(3),r.GetNullableString(4),r.GetNullableInt32(5),r.GetNullableString(6),r.GetString(7),r.GetDecimal(8),r.GetDecimal(9),r.GetDecimal(10),r.GetDecimal(11),r.GetInt32(12),Convert.ToBase64String((byte[])r[13]),r.GetDateTime(14),r.GetInt32(15),r.GetDecimal(16),r.GetDecimal(17),r.GetDecimal(18),r.GetDecimal(19),r.GetDecimal(20),r.GetNullableInt32(21),r.GetNullableDecimal(22));
    private static ScoreRunDto MapScoreRun(SqlDataReader r)=>new(r.GetInt64(0),r.GetInt32(1),r.GetInt32(2),r.GetDecimal(3),r.GetDecimal(4),r.GetDecimal(5),r.GetDecimal(6),r.GetDecimal(7),r.GetDecimal(8),r.GetDecimal(9),r.GetDecimal(10),r.GetDecimal(11),r.GetInt32(12),r.GetInt32(13),r.GetInt32(14),r.GetInt32(15),r.GetInt32(16),r.GetDateTime(17));
    private static CandidateListItemDto MapCandidate(SqlDataReader r)=>new(r.GetInt64(0),r.GetString(1),r.GetNullableString(2),r.GetNullableString(3),r.GetNullableString(4),r.GetString(5),r.GetNullableString(6),r.GetNullableInt32(7),r.GetNullableInt64(8),r.GetDateTime(9),Convert.ToBase64String((byte[])r[10]),r.FieldCount==12?r.GetNullableString(11):null);
    private static void ValidateJob(SaveJobRequest r){if(r.Name.Trim().Length is <1 or >200)throw new BusinessException("JOB_NAME_INVALID","岗位名称必须为1至200字。 ");if(r.Description?.Length>10000||r.Requirement.Trim().Length is <1 or >20000||r.SkillRequirement?.Length>10000||r.MinExperienceMonths is <0 or >720||r.EducationRequirement?.Length>100)throw new BusinessException("JOB_FIELD_INVALID","岗位字段长度或范围无效。 ");}
    private static void AddJobParameters(SqlCommand c,SaveJobRequest r){c.Parameters.Add("@Name",SqlDbType.NVarChar,200).Value=r.Name.Trim();AddNullable(c,"@Description",SqlDbType.NVarChar,-1,r.Description?.Trim());c.Parameters.Add("@Requirement",SqlDbType.NVarChar,-1).Value=r.Requirement.Trim();AddNullable(c,"@Skill",SqlDbType.NVarChar,-1,r.SkillRequirement?.Trim());c.Parameters.AddWithValue("@Months",(object?)r.MinExperienceMonths??DBNull.Value);AddNullable(c,"@Education",SqlDbType.NVarChar,100,r.EducationRequirement?.Trim());}
    private static string BuildInitialProfile(string name,string requirement)=>$"岗位名称：{name.Trim()}\r\n岗位职责：待HR完善\r\n核心技能：待HR完善\r\n产品与项目经验：待HR完善\r\n行业经验：待HR完善\r\n学历要求：待HR完善\r\n加分项：待HR完善\r\n排除项：待HR完善\r\n原岗位要求：{requirement.Trim()}";
    private static string HashContent(string content)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.Replace("\r\n","\n").Replace('\r','\n'))));
    private static byte[] ParseVersion(string? value){try{return Convert.FromBase64String(value??"");}catch{throw new BusinessException("INVALID_ROW_VERSION","并发版本格式无效。 ");}}
    private static string? NormalizePhone(string? value)=>string.IsNullOrWhiteSpace(value)?null:new string(value.Where(c=>!" -()（）".Contains(c)).ToArray());
    private static string? NormalizeEmail(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim().ToLowerInvariant();
    private static string? NormalizeOptionalText(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static int? ParseCandidateAge(string? rawText)
    {
        if(string.IsNullOrWhiteSpace(rawText))return null;
        var match=Regex.Match(rawText[..Math.Min(rawText.Length,800)],@"(?<!\d)(?<age>\d{2})\s*岁");
        if(!match.Success||!int.TryParse(match.Groups["age"].Value,out var age)||age is <16 or >99)return null;
        return age;
    }
    private static decimal? ParseExpectedMonthlySalaryUpper(string? rawText)
    {
        if(string.IsNullOrWhiteSpace(rawText))return null;
        var start=rawText.IndexOf("求职意向",StringComparison.Ordinal);
        var section=start>=0?rawText[start..Math.Min(rawText.Length,start+700)]:rawText[..Math.Min(rawText.Length,1000)];
        foreach(var marker in new[]{"个人优势","工作经历","教育经历","项目经历"})
        {
            var end=section.IndexOf(marker,StringComparison.Ordinal);if(end>0)section=section[..end];
        }
        var match=Regex.Match(section,@"(?<low>\d+(?:\.\d+)?)\s*[-－~～至]\s*(?<high>\d+(?:\.\d+)?)\s*(?<unit>千|万|元)\s*/\s*(?<period>月|年)");
        if(!match.Success)match=Regex.Match(section,@"(?<high>\d+(?:\.\d+)?)\s*(?<unit>千|万|元)\s*/\s*(?<period>月|年)");
        if(!match.Success||!decimal.TryParse(match.Groups["high"].Value,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var upper))return null;
        upper*=match.Groups["unit"].Value switch{"千"=>1000m,"万"=>10000m,_=>1m};
        if(match.Groups["period"].Value=="年")upper/=12m;
        return upper;
    }
    private static void AddNullable(SqlCommand c,string name,SqlDbType type,int size,object? value)=>c.Parameters.Add(name,type,size).Value=value??DBNull.Value;
    private static void AddDecimal(SqlCommand c,string name,decimal value){var p=c.Parameters.Add(name,SqlDbType.Decimal);p.Precision=5;p.Scale=2;p.Value=value;}
}

using System.Data;
using Microsoft.Data.SqlClient;
using RecruitAI.Application.Common;
using RecruitAI.Application.Tasks;
using RecruitAI.Infrastructure.Database;

namespace RecruitAI.Infrastructure.Tasks;

public sealed record AiTaskCleanupSettings(string ResumeRoot);

public sealed class SqlAiTaskService(SqlConnectionFactory factory, AiTaskCleanupSettings cleanupSettings) : IAiTaskService
{
    public async Task<AiTaskPage> ListAsync(int page, int pageSize, string? type, string? status, CancellationToken ct)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        const string where = "(@Type IS NULL OR TaskType=@Type) AND (@Status IS NULL OR (@Status='ACTIVE' AND Status IN ('WAITING','RUNNING','FAILED')) OR Status=@Status)";
        const string fields = "Id,TaskType,Status,RetryCount,MaxRetryCount,ErrorMessage,CreateTime,UpdateTime";
        var sql = $"WITH Paged AS(SELECT {fields},ROW_NUMBER() OVER(ORDER BY CASE Status WHEN 'RUNNING' THEN 0 WHEN 'WAITING' THEN 1 ELSE 2 END,CreateTime ASC,Id ASC) RowNo FROM dbo.AITask WHERE {where}) SELECT {fields} FROM Paged WHERE RowNo BETWEEN @Start AND @End ORDER BY RowNo";
        await using var connection = await factory.OpenAsync(ct);
        var normalizedType = type?.Trim().ToUpperInvariant(); var normalizedStatus = status?.Trim().ToUpperInvariant();
        await using var count = new SqlCommand("SELECT COUNT(*) FROM dbo.AITask WHERE " + where, connection);
        AddFilter(count, normalizedType, normalizedStatus);
        var total = Convert.ToInt32(await count.ExecuteScalarAsync(ct));
        await using var command = new SqlCommand(sql, connection); AddFilter(command, normalizedType, normalizedStatus);
        command.Parameters.AddWithValue("@Start", (page - 1) * pageSize + 1); command.Parameters.AddWithValue("@End", page * pageSize);
        await using var reader = await command.ExecuteReaderAsync(ct); var result = new List<AiTaskDto>();
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetNullableString(5), reader.GetDateTime(6), reader.GetDateTime(7)));
        return new(result, total, page, pageSize);
    }

    public async Task<AiTaskDetailDto?> GetAsync(long id, CancellationToken ct)
    {
        const string sql = """
            SELECT t.Id,t.TaskType,t.Status,t.RetryCount,t.MaxRetryCount,t.ErrorMessage,t.WorkerId,t.StartTime,t.FinishTime,
              t.ResumeId,r.CandidateId,c.Name,r.TalentId,r.FileName,LEN(r.RawText) RawLength,
              LEFT(ISNULL(r.RawText,N''),4000) RawPreview,r.IsCurrent,r.IsDeleted,t.CreateTime,t.UpdateTime,
              (SELECT COUNT(*) FROM dbo.Resume ar WHERE ar.CandidateId=r.CandidateId AND ar.IsDeleted=0) AffectedResumeCount
            FROM dbo.AITask t
            LEFT JOIN dbo.Resume r ON r.Id=t.ResumeId
            LEFT JOIN dbo.Candidate c ON c.Id=r.CandidateId
            WHERE t.Id=@Id
            """;
        await using var connection = await factory.OpenAsync(ct); await using var command = new SqlCommand(sql, connection); command.Parameters.AddWithValue("@Id", id);
        await using var reader = await command.ExecuteReaderAsync(ct); if (!await reader.ReadAsync(ct)) return null;
        var taskType = reader.GetString(1); var taskStatus = reader.GetString(2);
        int? rawLength = reader.IsDBNull(14) ? null : Convert.ToInt32(reader.GetValue(14));
        var affectedResumeCount = reader.IsDBNull(20) ? 0 : Convert.ToInt32(reader.GetValue(20));
        return new(reader.GetInt64(0), taskType, taskStatus, reader.GetInt32(3), reader.GetInt32(4), reader.GetNullableString(5),
            reader.GetNullableString(6), reader.GetNullableDateTime(7), reader.GetNullableDateTime(8), reader.GetNullableInt64(9),
            reader.GetNullableInt64(10), reader.GetNullableString(11), reader.GetNullableString(12), reader.GetNullableString(13),
            rawLength, reader.GetNullableString(15),
            taskStatus == "FAILED" && taskType is not ("RESUME_IMPORT" or "RESUME_PARSE"),
            taskStatus is "FAILED" or "CANCELLED",
            taskType == "RESUME_PARSE", affectedResumeCount, reader.GetDateTime(18), reader.GetDateTime(19));
    }

    public async Task RetryAsync(long id,CancellationToken ct)
    {
        const string sql="""
          SET NOCOUNT ON;
          UPDATE r SET Status='WAITING',ErrorMessage=NULL,StartTime=NULL,FinishTime=NULL,UpdateTime=SYSUTCDATETIME()
          FROM dbo.ScoringRunResult r JOIN dbo.AITask t ON t.ScoringRunId=r.ScoringRunId AND t.CandidateJobId=r.CandidateJobId
          WHERE t.Id=@Id AND t.Status='FAILED' AND t.TaskType='MATCH_SCORE';
          UPDATE dbo.AITask SET Status='WAITING',RetryCount=0,NextRunTime=SYSUTCDATETIME(),ErrorMessage=NULL,
            WorkerId=NULL,LockedUntil=NULL,StartTime=NULL,FinishTime=NULL,UpdateTime=SYSUTCDATETIME()
          WHERE Id=@Id AND Status='FAILED' AND TaskType NOT IN ('RESUME_IMPORT','RESUME_PARSE');
          SELECT @@ROWCOUNT;
          """;
        await using var connection=await factory.OpenAsync(ct);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@Id",id);
        if(Convert.ToInt32(await command.ExecuteScalarAsync(ct))!=1)throw new BusinessException("TASK_STATE_CONFLICT","该任务不可重试；简历导入失败必须由HR处理。",409);
    }

    public async Task CancelAsync(long id, CancellationToken ct)
    {
        const string sql="""
        SET NOCOUNT ON;
        UPDATE r SET Status='CANCELLED',FinishTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME()
        FROM dbo.ScoringRunResult r JOIN dbo.AITask t ON t.ScoringRunId=r.ScoringRunId AND t.CandidateJobId=r.CandidateJobId
        WHERE t.Id=@Id AND t.Status='WAITING' AND r.Status='WAITING';
        UPDATE dbo.AITask SET Status='CANCELLED',FinishTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME()
        WHERE Id=@Id AND Status='WAITING';
        SELECT @@ROWCOUNT;
        """;
        await using var connection=await factory.OpenAsync(ct);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@Id",id);
        if(Convert.ToInt32(await command.ExecuteScalarAsync(ct))!=1)throw new BusinessException("TASK_STATE_CONFLICT","只有等待中的任务可以取消。",409);
    }

    public async Task<int> CancelAllWaitingAsync(CancellationToken ct)
    {
        const string sql = """
            UPDATE r SET Status='CANCELLED',FinishTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME()
            FROM dbo.ScoringRunResult r JOIN dbo.AITask t ON t.ScoringRunId=r.ScoringRunId AND t.CandidateJobId=r.CandidateJobId
            WHERE t.Status='WAITING' AND r.Status='WAITING';
            UPDATE dbo.AITask SET Status='CANCELLED',ErrorMessage=N'HR在AI任务暂停后批量取消。',
              FinishTime=SYSUTCDATETIME(),WorkerId=NULL,LockedUntil=NULL,UpdateTime=SYSUTCDATETIME()
            WHERE Status='WAITING';
            SELECT @@ROWCOUNT;
            """;
        await using var connection = await factory.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    public async Task<int> ClearAllFailedAsync(long userId, CancellationToken ct)
    {
        var resumeParseIds=new List<long>();
        var total=0;
        await using(var connection=await factory.OpenAsync(ct))
        {
            await using(var count=new SqlCommand("SELECT COUNT(*) FROM dbo.AITask WHERE Status='FAILED'",connection))
                total=Convert.ToInt32(await count.ExecuteScalarAsync(ct));
            await using var find=new SqlCommand("SELECT Id FROM dbo.AITask WHERE Status='FAILED' AND TaskType='RESUME_PARSE' ORDER BY Id",connection);
            await using var reader=await find.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))resumeParseIds.Add(reader.GetInt64(0));
        }
        foreach(var taskId in resumeParseIds)
        {
            try{await PurgeAsync(taskId,userId,ct);}
            catch(BusinessException ex) when(ex.Code=="AI_TASK_NOT_FOUND"){}
        }
        await using(var connection=await factory.OpenAsync(ct))
        await using(var delete=new SqlCommand("DELETE dbo.AITask WHERE Status='FAILED'",connection))
            await delete.ExecuteNonQueryAsync(ct);
        return total;
    }

    public async Task PurgeAsync(long id, long userId, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        string taskType; string taskStatus; long? candidateId;
        await using (var find = new SqlCommand("SELECT t.TaskType,t.Status,r.CandidateId FROM dbo.AITask t LEFT JOIN dbo.Resume r ON r.Id=t.ResumeId WHERE t.Id=@Id", connection))
        {
            find.Parameters.AddWithValue("@Id", id); await using var reader = await find.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new BusinessException("AI_TASK_NOT_FOUND", "AI任务不存在。", 404);
            taskType = reader.GetString(0); taskStatus = reader.GetString(1); candidateId = reader.GetNullableInt64(2);
        }
        if (taskStatus is not ("FAILED" or "CANCELLED")) throw new BusinessException("AI_TASK_NOT_PURGEABLE", "只能清除失败或已取消的任务。", 409);
        if (taskType != "RESUME_PARSE")
        {
            await using var deleteTask = new SqlCommand("DELETE dbo.AITask WHERE Id=@Id AND Status IN ('FAILED','CANCELLED')", connection);
            deleteTask.Parameters.AddWithValue("@Id", id); await deleteTask.ExecuteNonQueryAsync(ct); return;
        }
        if (candidateId is null) throw new BusinessException("AI_TASK_RESUME_MISSING", "任务关联的简历或人才不存在。", 409);

        var relativePaths = new List<string>();
        await using (var paths = new SqlCommand("SELECT FilePath FROM dbo.Resume WHERE CandidateId=@CandidateId AND IsDeleted=0", connection))
        {
            paths.Parameters.AddWithValue("@CandidateId", candidateId.Value); await using var reader = await paths.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) relativePaths.Add(reader.GetString(0));
        }
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            const string purgeSql = """
                SELECT Id INTO #R FROM dbo.Resume WHERE CandidateId=@CandidateId;
                SELECT Id INTO #CJ FROM dbo.CandidateJob WHERE CandidateId=@CandidateId;
                SELECT DISTINCT ImportFileId INTO #IF FROM dbo.ResumeImportResult WHERE ResumeId IN(SELECT Id FROM #R);
                DELETE FROM dbo.CandidateJobReview WHERE CandidateJobId IN(SELECT Id FROM #CJ);
                DELETE FROM dbo.AITask WHERE ResumeId IN(SELECT Id FROM #R) OR CandidateJobId IN(SELECT Id FROM #CJ);
                DELETE FROM dbo.CleanupLog WHERE ObjectType='RESUME' AND ObjectId IN(SELECT Id FROM #R);
                DELETE FROM dbo.ResumeImportResult WHERE ResumeId IN(SELECT Id FROM #R);
                DELETE FROM dbo.ResumeJobSource WHERE ResumeId IN(SELECT Id FROM #R);
                DELETE FROM dbo.CandidateProfile WHERE CandidateId=@CandidateId;
                DELETE FROM dbo.CandidateSkill WHERE CandidateId=@CandidateId;
                DELETE FROM dbo.CandidateEducation WHERE CandidateId=@CandidateId;
                DELETE FROM dbo.CandidateWorkExperience WHERE CandidateId=@CandidateId;
                DELETE FROM dbo.CandidateProject WHERE CandidateId=@CandidateId;
                DELETE FROM dbo.CandidateJob WHERE CandidateId=@CandidateId;
                DELETE FROM dbo.Resume WHERE CandidateId=@CandidateId;
                DELETE FROM dbo.Candidate WHERE Id=@CandidateId;
                INSERT dbo.CleanupLog(ObjectType,ObjectId,Action,Reason,OperatorId,DetailJson)
                VALUES('CANDIDATE',@CandidateId,'PURGE_FAILED_RESUME_PARSE',N'HR确认无法处理AI解析失败简历，清除人才及全部简历。',@UserId,N'{"source":"AI_TASK"}');
                """;
            await using var purge = new SqlCommand(purgeSql, connection, transaction); purge.Parameters.AddWithValue("@CandidateId", candidateId.Value); purge.Parameters.AddWithValue("@UserId", userId);
            await purge.ExecuteNonQueryAsync(ct); await transaction.CommitAsync(ct);
        }
        catch { await transaction.RollbackAsync(ct); throw; }

        var root = Path.GetFullPath(cleanupSettings.ResumeRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var relativePath in relativePaths)
        {
            if (Path.IsPathRooted(relativePath)) continue;
            var full = Path.GetFullPath(Path.Combine(root, relativePath));
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full)) File.Delete(full);
        }
    }

    private async Task UpdateAsync(long id, string sql, string message, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct); await using var command = new SqlCommand(sql, connection); command.Parameters.AddWithValue("@Id", id);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new BusinessException("TASK_STATE_CONFLICT", message, 409);
    }
    private static void AddFilter(SqlCommand command, string? type, string? status)
    {
        command.Parameters.AddWithValue("@Type", (object?)type ?? DBNull.Value); command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
    }
}

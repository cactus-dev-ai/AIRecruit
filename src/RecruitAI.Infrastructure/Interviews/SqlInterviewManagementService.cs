using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using RecruitAI.Application.Common;
using RecruitAI.Application.Interviews;
using RecruitAI.Infrastructure.Database;

namespace RecruitAI.Infrastructure.Interviews;

public sealed record InterviewManagementSettings(string MiniProgramName, string FallbackCodeLookupKey, string AgentBaseUrl, string PublicUrl);

public sealed class SqlInterviewManagementService(
    SqlConnectionFactory connectionFactory, InterviewManagementSettings settings, InterviewInvitationNotifier notifier) : IInterviewManagementService
{
    private static readonly HashSet<string> PlanTypes = new(StringComparer.Ordinal) { "INTRO", "FIXED", "CONDITION", "FOLLOW_UP", "CANDIDATE_QA", "END" };
    private static readonly HashSet<string> SetTypes = new(StringComparer.Ordinal) { "FIXED", "RESUME_VERIFY", "GAP_PROBE", "KNOWLEDGE_PROBE", "FOLLOW_UP", "CANDIDATE_QA", "END" };

    public async Task<InterviewListPage> ListAsync(int page, int pageSize, string? status, string? keyword, CancellationToken ct)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        status = Clean(status)?.ToUpperInvariant(); keyword = Clean(keyword);
        const string sql = """
            WITH Filtered AS (
              SELECT InvitationId,InvitationStatus,ExpireTime,VerificationMethod,QuestionSetId,QuestionSetStatus,
                CandidateJobId,CandidateId,CandidateName,JobId,JobName,SessionId,SessionStatus,StartedTime,
                CompletedTime,OverallScore,Recommendation,LastBusinessTime
              FROM dbo.vw_InterviewManagementList
              WHERE (@Status IS NULL OR InvitationStatus=@Status OR SessionStatus=@Status OR QuestionSetStatus=@Status)
                AND (@Keyword IS NULL OR CandidateName LIKE N'%'+@Keyword+N'%' OR JobName LIKE N'%'+@Keyword+N'%')
            ), Paged AS (
              SELECT COUNT(*) OVER() TotalCount,*,ROW_NUMBER() OVER(ORDER BY LastBusinessTime DESC,InvitationId DESC) RowNo
              FROM Filtered
            )
            SELECT TotalCount,InvitationId,InvitationStatus,ExpireTime,VerificationMethod,QuestionSetId,QuestionSetStatus,
              CandidateJobId,CandidateId,CandidateName,JobId,JobName,SessionId,SessionStatus,StartedTime,CompletedTime,
              OverallScore,Recommendation,LastBusinessTime
            FROM Paged WHERE RowNo BETWEEN @Start AND @End ORDER BY RowNo;
            """;
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        AddNullable(command, "@Status", SqlDbType.VarChar, 30, status);
        AddNullable(command, "@Keyword", SqlDbType.NVarChar, 100, keyword);
        command.Parameters.AddWithValue("@Start", (page - 1) * pageSize + 1);
        command.Parameters.AddWithValue("@End", page * pageSize);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<InterviewListItemDto>(); var total = 0;
        while (await reader.ReadAsync(ct))
        {
            total = reader.GetInt32(0);
            result.Add(new(reader.GetInt64(1), reader.GetString(2), reader.GetDateTime(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt64(5), reader.GetString(6), reader.GetInt64(7), reader.GetInt64(8), reader.GetString(9),
                reader.GetInt64(10), reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetInt64(12),
                reader.IsDBNull(13) ? null : reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetDateTime(14),
                reader.IsDBNull(15) ? null : reader.GetDateTime(15), reader.IsDBNull(16) ? null : reader.GetDecimal(16),
                reader.IsDBNull(17) ? null : reader.GetString(17), reader.GetDateTime(18)));
        }
        return new(result, total, page, pageSize);
    }

    public async Task<IReadOnlyList<InterviewCandidateDto>> FindCandidatesAsync(string? keyword, int take, CancellationToken ct)
    {
        keyword = Clean(keyword); take = Math.Clamp(take, 1, 50);
        const string sql = """
            SELECT TOP(@Take) cj.Id,c.Id,c.Name,j.Id,j.Name,r.Id,cj.AIScore
            FROM dbo.CandidateJob cj
            JOIN dbo.Candidate c ON c.Id=cj.CandidateId AND c.IsDeleted=0
            JOIN dbo.Job j ON j.Id=cj.JobId AND j.IsDeleted=0
            JOIN dbo.Resume r ON r.CandidateId=c.Id AND r.IsCurrent=1 AND r.IsDeleted=0
            WHERE (@Keyword IS NULL OR c.Name LIKE N'%'+@Keyword+N'%' OR j.Name LIKE N'%'+@Keyword+N'%')
            ORDER BY cj.UpdateTime DESC,cj.Id DESC;
            """;
        await using var connection = await connectionFactory.OpenAsync(ct); await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Take", take); AddNullable(command, "@Keyword", SqlDbType.NVarChar, 100, keyword);
        await using var reader = await command.ExecuteReaderAsync(ct); var result = new List<InterviewCandidateDto>();
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4), reader.GetInt64(5), reader.IsDBNull(6) ? null : reader.GetDecimal(6)));
        return result;
    }

    public async Task<IReadOnlyList<InterviewPlanDto>> ListPlansAsync(long? jobId, CancellationToken ct)
    {
        const string sql = """
            SELECT p.Id,p.JobId,j.Name,p.Name,p.VersionNo,p.Status,p.DurationMinutes,p.MaxSessionMinutes,
              p.ConsentTextVersion,p.UpdateTime,p.RowVersion
            FROM dbo.InterviewPlan p JOIN dbo.Job j ON j.Id=p.JobId
            WHERE (@JobId IS NULL OR p.JobId=@JobId)
            ORDER BY p.UpdateTime DESC,p.Id DESC;
            """;
        await using var connection = await connectionFactory.OpenAsync(ct); await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@JobId", SqlDbType.BigInt).Value = jobId is null ? DBNull.Value : jobId.Value;
        await using var reader = await command.ExecuteReaderAsync(ct); var result = new List<InterviewPlanDto>();
        while (await reader.ReadAsync(ct)) result.Add(MapPlan(reader, []));
        return result;
    }

    public async Task<InterviewPlanDto?> GetPlanAsync(long id, CancellationToken ct)
    {
        const string planSql = """
            SELECT p.Id,p.JobId,j.Name,p.Name,p.VersionNo,p.Status,p.DurationMinutes,p.MaxSessionMinutes,
              p.ConsentTextVersion,p.UpdateTime,p.RowVersion
            FROM dbo.InterviewPlan p JOIN dbo.Job j ON j.Id=p.JobId WHERE p.Id=@Id;
            """;
        const string questionSql = """
            SELECT Id,SequenceNo,QuestionType,QuestionText,DimensionCode,MaxAnswerSeconds,MinTranscriptChars,IsScored,IsRequired
            FROM dbo.InterviewQuestion WHERE PlanId=@Id ORDER BY SequenceNo;
            """;
        await using var connection = await connectionFactory.OpenAsync(ct);
        InterviewPlanDto? plan;
        await using (var command = new SqlCommand(planSql, connection))
        {
            command.Parameters.AddWithValue("@Id", id); await using var reader = await command.ExecuteReaderAsync(ct);
            plan = await reader.ReadAsync(ct) ? MapPlan(reader, []) : null;
        }
        if (plan is null) return null;
        var questions = new List<InterviewQuestionInput>();
        await using (var command = new SqlCommand(questionSql, connection))
        {
            command.Parameters.AddWithValue("@Id", id); await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) questions.Add(new(reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetBoolean(7), reader.GetBoolean(8)));
        }
        return plan with { Questions = questions };
    }

    public async Task<long> CreatePlanAsync(SaveInterviewPlanRequest request, long userId, CancellationToken ct)
    {
        ValidatePlan(request);
        await using var connection = await connectionFactory.OpenAsync(ct); await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            const string sql = """
                DECLARE @Version INT=ISNULL((SELECT MAX(VersionNo)+1 FROM dbo.InterviewPlan WITH(UPDLOCK,HOLDLOCK) WHERE JobId=@JobId AND Name=@Name),1);
                INSERT dbo.InterviewPlan(JobId,Name,VersionNo,Status,DurationMinutes,MaxSessionMinutes,ConsentTextVersion,CreatedBy)
                VALUES(@JobId,@Name,@Version,'DRAFT',@Duration,@MaxDuration,@Consent,@UserId);
                SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
                """;
            await using var command = new SqlCommand(sql, connection, transaction); AddPlanParameters(command, request); command.Parameters.AddWithValue("@UserId", userId);
            var id = Convert.ToInt64(await command.ExecuteScalarAsync(ct));
            await ReplacePlanQuestionsAsync(connection, transaction, id, request.Questions, ct);
            await transaction.CommitAsync(ct); return id;
        }
        catch { await transaction.RollbackAsync(ct); throw; }
    }

    public async Task UpdatePlanAsync(long id, SaveInterviewPlanRequest request, CancellationToken ct)
    {
        ValidatePlan(request); var version = ParseVersion(request.RowVersion);
        await using var connection = await connectionFactory.OpenAsync(ct); await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            const string sql = """
                UPDATE dbo.InterviewPlan SET Name=@Name,DurationMinutes=@Duration,MaxSessionMinutes=@MaxDuration,
                  ConsentTextVersion=@Consent,UpdateTime=SYSUTCDATETIME()
                WHERE Id=@Id AND JobId=@JobId AND Status='DRAFT' AND RowVersion=@Version;
                """;
            await using var command = new SqlCommand(sql, connection, transaction); AddPlanParameters(command, request);
            command.Parameters.AddWithValue("@Id", id); command.Parameters.Add("@Version", SqlDbType.Timestamp).Value = version;
            if (await command.ExecuteNonQueryAsync(ct) != 1) throw Conflict("面试方案已变化或不是草稿状态。");
            await ReplacePlanQuestionsAsync(connection, transaction, id, request.Questions, ct);
            await transaction.CommitAsync(ct);
        }
        catch { await transaction.RollbackAsync(ct); throw; }
    }

    public async Task PublishPlanAsync(long id, long userId, CancellationToken ct)
    {
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @JobId BIGINT=(SELECT JobId FROM dbo.InterviewPlan WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id AND Status='DRAFT');
            IF @JobId IS NULL BEGIN ROLLBACK; RAISERROR(N'方案不存在或已发布。',16,1); RETURN; END;
            IF NOT EXISTS(SELECT 1 FROM dbo.InterviewQuestion WHERE PlanId=@Id) BEGIN ROLLBACK; RAISERROR(N'方案至少需要一道题目。',16,1); RETURN; END;
            UPDATE dbo.InterviewPlan SET Status='ARCHIVED',UpdateTime=SYSUTCDATETIME() WHERE JobId=@JobId AND Status='ACTIVE';
            UPDATE dbo.InterviewPlan SET Status='ACTIVE',PublishedBy=@UserId,PublishTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME() WHERE Id=@Id;
            COMMIT;
            """;
        await using var connection = await connectionFactory.OpenAsync(ct); await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id); command.Parameters.AddWithValue("@UserId", userId); await command.ExecuteNonQueryAsync(ct);
    }

    public async Task StopPlanAsync(long id,CancellationToken ct)
    {
        const string sql="UPDATE dbo.InterviewPlan SET Status='ARCHIVED',UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND Status='ACTIVE'";
        await using var connection=await connectionFactory.OpenAsync(ct);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@Id",id);
        if(await command.ExecuteNonQueryAsync(ct)!=1)throw new BusinessException("INTERVIEW_PLAN_STOP_INVALID","只有已启用的方案可以停用。",409);
    }

    public async Task<long> ClonePlanAsync(long id,long userId,CancellationToken ct)
    {
        await using var connection=await connectionFactory.OpenAsync(ct);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            const string sql="""
                DECLARE @JobId BIGINT,@Name NVARCHAR(200),@Version INT;
                SELECT @JobId=JobId,@Name=Name FROM dbo.InterviewPlan WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;
                IF @JobId IS NULL RAISERROR(N'原面试方案不存在。',16,1);
                SELECT @Version=ISNULL(MAX(VersionNo)+1,1) FROM dbo.InterviewPlan WHERE JobId=@JobId AND Name=@Name;
                INSERT dbo.InterviewPlan(JobId,Name,VersionNo,Status,DurationMinutes,MaxSessionMinutes,ScoreRuleSnapshotJson,ConsentTextVersion,CreatedBy)
                SELECT JobId,Name,@Version,'DRAFT',DurationMinutes,MaxSessionMinutes,ScoreRuleSnapshotJson,ConsentTextVersion,@UserId FROM dbo.InterviewPlan WHERE Id=@Id;
                DECLARE @NewId BIGINT=SCOPE_IDENTITY();
                INSERT dbo.InterviewQuestion(PlanId,SequenceNo,QuestionType,QuestionText,DimensionCode,MaxAnswerSeconds,MinTranscriptChars,FollowUpPolicyJson,IsScored,IsRequired)
                SELECT @NewId,SequenceNo,QuestionType,QuestionText,DimensionCode,MaxAnswerSeconds,MinTranscriptChars,FollowUpPolicyJson,IsScored,IsRequired FROM dbo.InterviewQuestion WHERE PlanId=@Id;
                SELECT @NewId;
                """;
            await using var command=new SqlCommand(sql,connection,transaction);command.Parameters.AddWithValue("@Id",id);command.Parameters.AddWithValue("@UserId",userId);var newId=Convert.ToInt64(await command.ExecuteScalarAsync(ct));await transaction.CommitAsync(ct);return newId;
        }
        catch{await transaction.RollbackAsync(ct);throw;}
    }

    public async Task DeletePlanAsync(long id,CancellationToken ct)
    {
        await using var connection=await connectionFactory.OpenAsync(ct);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            const string checkSql="SELECT Status,CASE WHEN EXISTS(SELECT 1 FROM dbo.InterviewQuestionSet WHERE PlanId=@Id) THEN 1 ELSE 0 END FROM dbo.InterviewPlan WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id";
            string status;bool referenced;
            await using(var check=new SqlCommand(checkSql,connection,transaction)){check.Parameters.AddWithValue("@Id",id);await using var reader=await check.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))throw new BusinessException("INTERVIEW_PLAN_NOT_FOUND","面试方案不存在。",404);status=reader.GetString(0);referenced=reader.GetInt32(1)==1;}
            if(status!="DRAFT")throw new BusinessException("INTERVIEW_PLAN_DELETE_INVALID","只有草稿方案可以删除；已启用或历史方案请停用并保留审计。",409);
            if(referenced)throw new BusinessException("INTERVIEW_PLAN_REFERENCED","方案已被候选人题集引用，不能删除。",409);
            await using(var questions=new SqlCommand("DELETE dbo.InterviewQuestion WHERE PlanId=@Id",connection,transaction)){questions.Parameters.AddWithValue("@Id",id);await questions.ExecuteNonQueryAsync(ct);}
            await using(var plan=new SqlCommand("SET ANSI_NULLS ON;SET QUOTED_IDENTIFIER ON;SET ANSI_PADDING ON;SET ANSI_WARNINGS ON;SET ARITHABORT ON;SET CONCAT_NULL_YIELDS_NULL ON;SET NUMERIC_ROUNDABORT OFF;DELETE dbo.InterviewPlan WHERE Id=@Id AND Status='DRAFT'",connection,transaction)){plan.Parameters.AddWithValue("@Id",id);if(await plan.ExecuteNonQueryAsync(ct)!=1)throw Conflict("面试方案状态已经变化。");}
            await transaction.CommitAsync(ct);
        }
        catch{await transaction.RollbackAsync(ct);throw;}
    }

    public async Task<long> CreateQuestionSetDraftAsync(CreateQuestionSetRequest request, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct); await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            const string infoSql = """
                SELECT r.Id,CONVERT(VARCHAR(100),r.RowVersion,1),CONVERT(VARCHAR(200),cj.RowVersion,1),sc.Version,
                  kd.Id,kd.Version,p.Id,j.Name,p.Name,cj.AIScore,cj.SkillScore,cj.ProjectScore,cj.ExperienceScore,cj.EducationScore,
                  cj.Strengths,cj.Weaknesses,cj.Recommendation,cj.AIReport
                FROM dbo.CandidateJob cj
                JOIN dbo.Candidate c ON c.Id=cj.CandidateId AND c.IsDeleted=0
                  AND c.PhoneNormalized IS NOT NULL AND LEN(c.PhoneNormalized)>0
                JOIN dbo.Resume r ON r.CandidateId=cj.CandidateId AND r.IsCurrent=1 AND r.IsDeleted=0
                JOIN dbo.ScoreConfig sc ON sc.JobId=cj.JobId
                JOIN dbo.Job j ON j.Id=cj.JobId AND j.IsDeleted=0
                JOIN dbo.InterviewPlan p ON p.JobId=cj.JobId AND p.Status='ACTIVE' AND (@PlanId=0 OR p.Id=@PlanId)
                LEFT JOIN dbo.JobKnowledgeDocument jkd ON jkd.JobId=cj.JobId AND jkd.IsPrimary=1
                LEFT JOIN dbo.KnowledgeDocument kd ON kd.Id=jkd.KnowledgeDocumentId AND kd.IsDeleted=0
                WHERE cj.Id=@CandidateJobId AND cj.IsFavorite=1 AND cj.AIScore IS NOT NULL;
                """;
            long resumeId, planId; string resumeVersion, analysisVersion, jobName, planName; int scoreVersion; long? documentId; int? documentVersion;
            decimal? aiScore,skillScore,projectScore,experienceScore,educationScore; string? strengths,weaknesses,recommendation,aiReport;
            await using (var info = new SqlCommand(infoSql, connection, transaction))
            {
                info.Parameters.AddWithValue("@PlanId", request.PlanId); info.Parameters.AddWithValue("@CandidateJobId", request.CandidateJobId);
                await using var reader = await info.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) throw new BusinessException("INTERVIEW_SOURCE_INVALID", "只能从简历收藏发起面试；候选人必须有手机号和评分结果，岗位必须有已启用的面试方案。", 409);
                resumeId=reader.GetInt64(0); resumeVersion=reader.GetString(1); analysisVersion=reader.GetString(2); scoreVersion=reader.GetInt32(3);
                documentId=reader.IsDBNull(4)?null:reader.GetInt64(4); documentVersion=reader.IsDBNull(5)?null:reader.GetInt32(5);
                planId=reader.GetInt64(6);jobName=reader.GetString(7);planName=reader.GetString(8);
                aiScore=M(reader,9);skillScore=M(reader,10);projectScore=M(reader,11);experienceScore=M(reader,12);educationScore=M(reader,13);
                strengths=N(reader,14);weaknesses=N(reader,15);recommendation=N(reader,16);aiReport=N(reader,17);
            }
            var planQuestions=new List<object>();
            await using(var planCommand=new SqlCommand("SELECT SequenceNo,QuestionType,QuestionText,DimensionCode,MaxAnswerSeconds,IsRequired,IsScored FROM dbo.InterviewQuestion WHERE PlanId=@PlanId ORDER BY SequenceNo",connection,transaction))
            {planCommand.Parameters.AddWithValue("@PlanId",planId);await using var reader=await planCommand.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))planQuestions.Add(new{sequence_no=reader.GetInt32(0),question_type=reader.GetString(1),question_text=reader.GetString(2),dimension_code=N(reader,3),max_answer_seconds=reader.GetInt32(4),is_required=reader.GetBoolean(5),is_scored=reader.GetBoolean(6)});}
            if(planQuestions.Count==0)throw new BusinessException("INTERVIEW_PLAN_EMPTY","已启用的面试方案没有题目。",409);
            var aiContext=new{job_name=jobName,plan_name=planName,score_version=scoreVersion,scores=new{overall=aiScore,skill=skillScore,project=projectScore,experience=experienceScore,education=educationScore},strengths,weaknesses,recommendation,score_report=aiReport,plan_questions=planQuestions};
            var generated=await GenerateAiQuestionSetAsync(aiContext,ct);
            var inputHash = Sha256Hex($"{request.CandidateJobId}|{planId}|{resumeVersion}|{analysisVersion}|{scoreVersion}|{Guid.NewGuid():N}");
            const string insertSql = """
                DECLARE @Revision INT=ISNULL((SELECT MAX(RevisionNo)+1 FROM dbo.InterviewQuestionSet WITH(UPDLOCK,HOLDLOCK) WHERE CandidateJobId=@CandidateJobId AND PlanId=@PlanId),1);
                INSERT dbo.InterviewQuestionSet(CandidateJobId,PlanId,RevisionNo,Status,ResumeId,ResumeProfileVersion,
                  CandidateJobAnalysisVersion,JobProfileDocumentId,JobProfileVersion,ScoreConfigVersion,InputVersionHash,GeneratedByModel,PromptVersion)
                VALUES(@CandidateJobId,@PlanId,@Revision,'DRAFT',@ResumeId,@ResumeVersion,@AnalysisVersion,@DocumentId,@DocumentVersion,@ScoreVersion,@InputHash,@Model,@PromptVersion);
                SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
                """;
            long id;
            await using (var insert = new SqlCommand(insertSql, connection, transaction))
            {
                insert.Parameters.AddWithValue("@CandidateJobId", request.CandidateJobId); insert.Parameters.AddWithValue("@PlanId", planId);
                insert.Parameters.AddWithValue("@ResumeId", resumeId); insert.Parameters.AddWithValue("@ResumeVersion", resumeVersion); insert.Parameters.AddWithValue("@AnalysisVersion", analysisVersion);
                insert.Parameters.Add("@DocumentId", SqlDbType.BigInt).Value=documentId is null?DBNull.Value:documentId.Value;
                insert.Parameters.Add("@DocumentVersion", SqlDbType.Int).Value=documentVersion is null?DBNull.Value:documentVersion.Value;
                insert.Parameters.AddWithValue("@ScoreVersion", scoreVersion); insert.Parameters.AddWithValue("@InputHash", inputHash);
                insert.Parameters.Add("@Model",SqlDbType.VarChar,100).Value=generated.Model;insert.Parameters.Add("@PromptVersion",SqlDbType.VarChar,50).Value=generated.PromptVersion;
                id=Convert.ToInt64(await insert.ExecuteScalarAsync(ct));
            }
            var evidence=string.Join("；",new[]{recommendation,strengths,weaknesses}.Where(x=>!string.IsNullOrWhiteSpace(x))).Trim();if(evidence.Length>2000)evidence=evidence[..2000];
            foreach(var q in generated.Questions.Select((value,index)=>(value,index)))
            {
                const string questionSql="""
                  INSERT dbo.InterviewSessionQuestion(QuestionSetId,SequenceNo,QuestionType,QuestionText,DimensionCode,GenerationReason,ExpectedEvidenceJson,MaxAnswerSeconds,IsRequired,IsScored,Status,GeneratedByModel,PromptVersion)
                  VALUES(@SetId,@Seq,@Type,@Text,@Dimension,@Reason,@Evidence,@Seconds,@Required,@Scored,'DRAFT',@Model,@PromptVersion);
                  SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
                  """;
                await using var insertQuestion=new SqlCommand(questionSql,connection,transaction);insertQuestion.Parameters.AddWithValue("@SetId",id);insertQuestion.Parameters.AddWithValue("@Seq",q.index+1);insertQuestion.Parameters.AddWithValue("@Type",q.value.QuestionType);insertQuestion.Parameters.Add("@Text",SqlDbType.NVarChar,2000).Value=q.value.QuestionText;insertQuestion.Parameters.Add("@Dimension",SqlDbType.VarChar,50).Value=(object?)q.value.DimensionCode??DBNull.Value;insertQuestion.Parameters.Add("@Reason",SqlDbType.NVarChar,1000).Value=(object?)q.value.GenerationReason??DBNull.Value;insertQuestion.Parameters.Add("@Evidence",SqlDbType.NVarChar,-1).Value=(object?)q.value.ExpectedEvidenceJson??DBNull.Value;insertQuestion.Parameters.AddWithValue("@Seconds",q.value.MaxAnswerSeconds);insertQuestion.Parameters.AddWithValue("@Required",q.value.IsRequired);insertQuestion.Parameters.AddWithValue("@Scored",q.value.IsScored);insertQuestion.Parameters.AddWithValue("@Model",generated.Model);insertQuestion.Parameters.AddWithValue("@PromptVersion",generated.PromptVersion);var questionId=Convert.ToInt64(await insertQuestion.ExecuteScalarAsync(ct));
                if(evidence.Length>0){await using var source=new SqlCommand("INSERT dbo.InterviewQuestionSource(SessionQuestionId,SourceType,SourceEntityId,SourceVersion,EvidenceText,ContentHash) VALUES(@QuestionId,'MATCH_ANALYSIS',@CandidateJobId,@Version,@Evidence,@Hash)",connection,transaction);source.Parameters.AddWithValue("@QuestionId",questionId);source.Parameters.AddWithValue("@CandidateJobId",request.CandidateJobId);source.Parameters.AddWithValue("@Version",analysisVersion);source.Parameters.Add("@Evidence",SqlDbType.NVarChar,2000).Value=evidence;source.Parameters.AddWithValue("@Hash",Sha256Hex(evidence));await source.ExecuteNonQueryAsync(ct);}
            }
            await transaction.CommitAsync(ct); return id;
        }
        catch { await transaction.RollbackAsync(ct); throw; }
    }

    public async Task<QuestionSetDetailDto?> GetQuestionSetAsync(long id, CancellationToken ct)
    {
        const string headerSql = """
            SELECT qs.Id,qs.Status,qs.RevisionNo,qs.CandidateJobId,c.Id,c.Name,j.Id,j.Name,p.Id,p.Name,qs.ResumeId,qs.UpdateTime,qs.RowVersion
            FROM dbo.InterviewQuestionSet qs JOIN dbo.CandidateJob cj ON cj.Id=qs.CandidateJobId
            JOIN dbo.Candidate c ON c.Id=cj.CandidateId JOIN dbo.Job j ON j.Id=cj.JobId JOIN dbo.InterviewPlan p ON p.Id=qs.PlanId
            WHERE qs.Id=@Id;
            """;
        const string detailSql = """
            SELECT Id,SequenceNo,QuestionType,QuestionText,DimensionCode,GenerationReason,ExpectedEvidenceJson,MaxAnswerSeconds,IsRequired,IsScored,Status,RowVersion
            FROM dbo.InterviewSessionQuestion WHERE QuestionSetId=@Id ORDER BY SequenceNo;
            """;
        await using var connection=await connectionFactory.OpenAsync(ct); QuestionSetDetailDto? result;
        await using(var command=new SqlCommand(headerSql,connection))
        { command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))return null;
          result=new(reader.GetInt64(0),reader.GetString(1),reader.GetInt32(2),reader.GetInt64(3),reader.GetInt64(4),reader.GetString(5),reader.GetInt64(6),reader.GetString(7),reader.GetInt64(8),reader.GetString(9),reader.GetInt64(10),reader.GetDateTime(11),VersionString((byte[])reader[12]),[]); }
        var questions=new List<QuestionSetQuestionDto>();
        await using(var command=new SqlCommand(detailSql,connection))
        {command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))questions.Add(new(reader.GetInt64(0),reader.GetInt32(1),reader.GetString(2),reader.GetString(3),reader.IsDBNull(4)?null:reader.GetString(4),reader.IsDBNull(5)?null:reader.GetString(5),reader.IsDBNull(6)?null:reader.GetString(6),reader.GetInt32(7),reader.GetBoolean(8),reader.GetBoolean(9),reader.GetString(10),VersionString((byte[])reader[11])));}
        return result with {Questions=questions};
    }

    public async Task UpdateQuestionSetAsync(long id, UpdateQuestionSetRequest request, CancellationToken ct)
    {
        ValidateSetQuestions(request.Questions); var version=ParseVersion(request.RowVersion);
        await using var connection=await connectionFactory.OpenAsync(ct);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            await using(var update=new SqlCommand("UPDATE dbo.InterviewQuestionSet SET UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND Status='DRAFT' AND RowVersion=@Version",connection,transaction))
            {update.Parameters.AddWithValue("@Id",id);update.Parameters.Add("@Version",SqlDbType.Timestamp).Value=version;if(await update.ExecuteNonQueryAsync(ct)!=1)throw Conflict("题集已变化或已冻结。");}
            await using(var delete=new SqlCommand("DELETE dbo.InterviewQuestionSource WHERE SessionQuestionId IN(SELECT Id FROM dbo.InterviewSessionQuestion WHERE QuestionSetId=@Id); DELETE dbo.InterviewSessionQuestion WHERE QuestionSetId=@Id",connection,transaction)){delete.Parameters.AddWithValue("@Id",id);await delete.ExecuteNonQueryAsync(ct);}
            foreach(var q in request.Questions)
            {await using var insert=new SqlCommand("INSERT dbo.InterviewSessionQuestion(QuestionSetId,SequenceNo,QuestionType,QuestionText,DimensionCode,GenerationReason,ExpectedEvidenceJson,MaxAnswerSeconds,IsRequired,IsScored,Status,GeneratedByModel,PromptVersion) VALUES(@Id,@Seq,@Type,@Text,@Dimension,@Reason,@Evidence,@Seconds,@Required,@Scored,'DRAFT','HR_EDITED','web-v1')",connection,transaction);AddSetQuestionParameters(insert,id,q);await insert.ExecuteNonQueryAsync(ct);}
            await transaction.CommitAsync(ct);
        }
        catch{await transaction.RollbackAsync(ct);throw;}
    }

    public async Task ConfirmQuestionSetAsync(long id, long userId, CancellationToken ct)
    {
        await using var connection=await connectionFactory.OpenAsync(ct);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            var text=new StringBuilder();await using(var read=new SqlCommand("SELECT SequenceNo,QuestionType,QuestionText,ISNULL(DimensionCode,'') FROM dbo.InterviewSessionQuestion WHERE QuestionSetId=@Id ORDER BY SequenceNo",connection,transaction)){read.Parameters.AddWithValue("@Id",id);await using var reader=await read.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))text.Append(reader.GetInt32(0)).Append('|').Append(reader.GetString(1)).Append('|').Append(reader.GetString(2)).Append('|').Append(reader.GetString(3)).Append('\n');}
            if(text.Length==0)throw new BusinessException("QUESTION_SET_EMPTY","题集至少需要一道题目。",409);var hash=Sha256Hex(text.ToString());
            await using(var questions=new SqlCommand("UPDATE dbo.InterviewSessionQuestion SET Status='CONFIRMED',ConfirmedBy=@UserId,ConfirmedTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME() WHERE QuestionSetId=@Id AND Status='DRAFT'",connection,transaction)){questions.Parameters.AddWithValue("@Id",id);questions.Parameters.AddWithValue("@UserId",userId);await questions.ExecuteNonQueryAsync(ct);}
            await using(var set=new SqlCommand("UPDATE dbo.InterviewQuestionSet SET Status='CONFIRMED',QuestionSetHash=@Hash,ConfirmedBy=@UserId,ConfirmedTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND Status='DRAFT'",connection,transaction)){set.Parameters.AddWithValue("@Id",id);set.Parameters.AddWithValue("@UserId",userId);set.Parameters.AddWithValue("@Hash",hash);if(await set.ExecuteNonQueryAsync(ct)!=1)throw Conflict("题集不是可冻结状态。");}
            await transaction.CommitAsync(ct);
        }catch{await transaction.RollbackAsync(ct);throw;}
    }

    public async Task<CreatedInvitationDto> CreateInvitationAsync(CreateInvitationRequest request, long userId, CancellationToken ct)
    {
        var channels=(request.NotificationChannels??Array.Empty<string>()).Select(x=>(x??"").Trim().ToUpperInvariant()).Where(x=>x.Length>0).Distinct(StringComparer.Ordinal).ToArray();
        if(channels.Any(x=>x is not ("EMAIL" or "SMS")))throw new BusinessException("NOTIFICATION_CHANNEL_INVALID","通知方式只允许 EMAIL 或 SMS。");
        var hours=Math.Clamp(request.ValidHours,1,168);var code=GenerateCode();var salt=RandomNumberGenerator.GetBytes(32);
        if(channels.Contains("SMS",StringComparer.Ordinal)&&hours!=72)throw new BusinessException("SMS_VALIDITY_MISMATCH","短信模板固定写明72小时，选择短信通知时邀请有效期必须为72小时。");
        var verifier=SHA256.HashData(Combine(salt,Encoding.UTF8.GetBytes(code)));var lookupKey=Encoding.UTF8.GetBytes(settings.FallbackCodeLookupKey);
        if(lookupKey.Length<16)throw new InvalidOperationException("Interview fallback lookup key must contain at least 16 bytes.");
        var lookup=new HMACSHA256(lookupKey).ComputeHash(Encoding.UTF8.GetBytes(code));
        const string sql="""
            INSERT dbo.InterviewInvitation(CandidateJobId,PlanId,QuestionSetId,FallbackCodeVerifier,FallbackCodeSalt,
              FallbackCodeLookupHash,Status,DeliveryChannel,ExpireTime,CreatedBy)
            SELECT CandidateJobId,PlanId,Id,@Verifier,@Salt,@Lookup,'CREATED','MANUAL',DATEADD(HOUR,@Hours,SYSUTCDATETIME()),@UserId
            FROM dbo.InterviewQuestionSet qs WHERE qs.Id=@SetId AND qs.Status='CONFIRMED'
              AND NOT EXISTS(SELECT 1 FROM dbo.InterviewInvitation i WHERE i.QuestionSetId=qs.Id AND i.IsDeleted=0 AND i.Status NOT IN('REVOKED','EXPIRED') AND i.ExpireTime>SYSUTCDATETIME());
            IF @@ROWCOUNT=0 RAISERROR(N'题集未冻结或已有有效邀请。',16,1);
            DECLARE @InvitationId BIGINT=CAST(SCOPE_IDENTITY() AS BIGINT);
            SELECT i.Id,i.ExpireTime,c.Name,c.EmailNormalized,c.PhoneNormalized,j.Name
            FROM dbo.InterviewInvitation i JOIN dbo.CandidateJob cj ON cj.Id=i.CandidateJobId
            JOIN dbo.Candidate c ON c.Id=cj.CandidateId JOIN dbo.Job j ON j.Id=cj.JobId WHERE i.Id=@InvitationId;
            """;
        await using var connection=await connectionFactory.OpenAsync(ct);await using var command=new SqlCommand(sql,connection);
        command.Parameters.AddWithValue("@SetId",request.QuestionSetId);command.Parameters.Add("@Verifier",SqlDbType.Binary,32).Value=verifier;command.Parameters.Add("@Salt",SqlDbType.Binary,32).Value=salt;command.Parameters.Add("@Lookup",SqlDbType.Binary,32).Value=lookup;command.Parameters.AddWithValue("@Hours",hours);command.Parameters.AddWithValue("@UserId",userId);
        long id;DateTime expire;string candidateName;string? email;string? phone;string jobName;
        await using(var reader=await command.ExecuteReaderAsync(ct)){if(!await reader.ReadAsync(ct))throw new BusinessException("INVITATION_CREATE_FAILED","邀请创建失败。",409);id=reader.GetInt64(0);expire=reader.GetDateTime(1);candidateName=reader.GetString(2);email=reader.IsDBNull(3)?null:reader.GetString(3);phone=reader.IsDBNull(4)?null:reader.GetString(4);jobName=reader.GetString(5);}
        var content=$"请使用微信打开 {settings.PublicUrl} 参加 AI 语音面试，输入本人手机号和邀请码 {code} 完成验证。邀请码仅用于本次邀请，请勿转发。";
        if(channels.Length==0)return new(id,settings.MiniProgramName,code,expire,content,settings.PublicUrl,Array.Empty<InvitationDeliveryDto>());
        var tasks=channels.Select(channel=>notifier.SendAsync(new(id,channel,candidateName,jobName,channel=="EMAIL"?email??"":phone??"",settings.PublicUrl,code,expire),ct));
        var results=await Task.WhenAll(tasks);await SaveDeliveryResultsAsync(id,results,email,phone,ct);
        var deliveries=results.Select(x=>new InvitationDeliveryDto(x.Channel,x.Sent?"SENT":"FAILED",x.MaskedDestination,x.ErrorCode,x.ErrorMessage)).ToArray();
        return new(id,settings.MiniProgramName,code,expire,content,settings.PublicUrl,deliveries);
    }

    private async Task SaveDeliveryResultsAsync(long invitationId,IReadOnlyList<InvitationNotificationResult> results,string? email,string? phone,CancellationToken ct)
    {
        await using var connection=await connectionFactory.OpenAsync(ct);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(ct);
        foreach(var result in results)
        {
            var destination=result.Channel=="EMAIL"?email??"":phone??"";var hash=SHA256.HashData(Encoding.UTF8.GetBytes(destination.Trim().ToLowerInvariant()));
            const string sql="""
              MERGE dbo.InterviewInvitationDelivery AS target
              USING(SELECT @InvitationId InvitationId,@Channel Channel) source ON target.InvitationId=source.InvitationId AND target.Channel=source.Channel
              WHEN MATCHED THEN UPDATE SET DestinationHash=@Hash,MaskedDestination=@Masked,Status=@Status,AttemptCount=target.AttemptCount+1,Provider=@Provider,ProviderMessageId=@ProviderId,ErrorCode=@ErrorCode,ErrorMessage=@ErrorMessage,LastAttemptTime=SYSUTCDATETIME(),SentTime=CASE WHEN @Status='SENT' THEN SYSUTCDATETIME() ELSE NULL END,UpdateTime=SYSUTCDATETIME()
              WHEN NOT MATCHED THEN INSERT(InvitationId,Channel,DestinationHash,MaskedDestination,Status,AttemptCount,Provider,ProviderMessageId,ErrorCode,ErrorMessage,LastAttemptTime,SentTime)
                VALUES(@InvitationId,@Channel,@Hash,@Masked,@Status,1,@Provider,@ProviderId,@ErrorCode,@ErrorMessage,SYSUTCDATETIME(),CASE WHEN @Status='SENT' THEN SYSUTCDATETIME() ELSE NULL END);
              """;
            await using var command=new SqlCommand(sql,connection,transaction);command.Parameters.AddWithValue("@InvitationId",invitationId);command.Parameters.AddWithValue("@Channel",result.Channel);command.Parameters.Add("@Hash",SqlDbType.Binary,32).Value=hash;command.Parameters.AddWithValue("@Masked",result.MaskedDestination);command.Parameters.AddWithValue("@Status",result.Sent?"SENT":"FAILED");command.Parameters.AddWithValue("@Provider",result.Provider);AddNullable(command,"@ProviderId",SqlDbType.NVarChar,200,result.ProviderMessageId);AddNullable(command,"@ErrorCode",SqlDbType.VarChar,100,result.ErrorCode);AddNullable(command,"@ErrorMessage",SqlDbType.NVarChar,1000,result.ErrorMessage);await command.ExecuteNonQueryAsync(ct);
        }
        var sent=results.Where(x=>x.Sent).ToArray();if(sent.Length>0)
        {
            var primary=sent.Any(x=>x.Channel=="EMAIL")?"EMAIL":"SMS";var masked=string.Join(" / ",sent.Select(x=>x.MaskedDestination));if(masked.Length>100)masked=masked[..100];
            await using var update=new SqlCommand("UPDATE dbo.InterviewInvitation SET Status='NOTIFIED',DeliveryChannel=@Channel,MaskedDestination=@Masked,NotifiedTime=COALESCE(NotifiedTime,SYSUTCDATETIME()),UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND Status='CREATED'",connection,transaction);update.Parameters.AddWithValue("@Channel",primary);update.Parameters.AddWithValue("@Masked",masked);update.Parameters.AddWithValue("@Id",invitationId);await update.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    public async Task RevokeInvitationAsync(long id, long userId, CancellationToken ct)
    {
        const string sql="UPDATE dbo.InterviewInvitation SET Status='REVOKED',RevokeTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND Status IN('CREATED','NOTIFIED','OPENED','VERIFIED')";
        await using var connection=await connectionFactory.OpenAsync(ct);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@Id",id);if(await command.ExecuteNonQueryAsync(ct)!=1)throw Conflict("当前邀请不能撤销。");
    }

    public async Task DeleteInvitationAsync(long id, long userId, CancellationToken ct)
    {
        await using var connection=await connectionFactory.OpenAsync(ct);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            const string checkSql="SELECT i.IsDeleted,(SELECT TOP 1 s.Status FROM dbo.InterviewSession s WHERE s.InvitationId=i.Id ORDER BY s.RetakeNo DESC) FROM dbo.InterviewInvitation i WITH(UPDLOCK,HOLDLOCK) WHERE i.Id=@Id";
            bool deleted;string? sessionStatus;
            await using(var check=new SqlCommand(checkSql,connection,transaction)){check.Parameters.AddWithValue("@Id",id);await using var reader=await check.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))throw new BusinessException("INTERVIEW_INVITATION_NOT_FOUND","面试邀请不存在。",404);deleted=reader.GetBoolean(0);sessionStatus=N(reader,1);}
            if(deleted){await transaction.CommitAsync(ct);return;}
            if(sessionStatus is "DEVICE_CHECK" or "IN_PROGRESS" or "PAUSED" or "PROCESSING")throw new BusinessException("INTERVIEW_DELETE_ACTIVE","面试正在进行或报告正在生成，不能删除。",409);
            const string sql="""
              UPDATE dbo.InterviewInvitation SET IsDeleted=1,DeletedBy=@UserId,DeletedTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND IsDeleted=0;
              UPDATE dbo.InterviewAccessSession SET Status='REVOKED',RevokedTime=COALESCE(RevokedTime,SYSUTCDATETIME()),UpdateTime=SYSUTCDATETIME() WHERE InvitationId=@Id AND Status IN('TEMPORARY','BOUND');
              INSERT dbo.InterviewAuditLog(ActorType,ActorUserId,ActionCode,EntityType,EntityId,InvitationId,ResultCode,DetailJson)
              VALUES('HR',@UserId,'INVITATION_SOFT_DELETE','INTERVIEW_INVITATION',@Id,@Id,'SUCCESS',N'{"preserveEvidence":true}');
              """;
            await using(var command=new SqlCommand(sql,connection,transaction)){command.Parameters.AddWithValue("@Id",id);command.Parameters.AddWithValue("@UserId",userId);await command.ExecuteNonQueryAsync(ct);}
            await transaction.CommitAsync(ct);
        }
        catch{await transaction.RollbackAsync(ct);throw;}
    }

    public async Task<InterviewDetailDto?> GetDetailAsync(long invitationId, CancellationToken ct)
    {
        const string sql="""
            SELECT i.Id,i.Status,i.ExpireTime,c.Name,c.Phone,c.Email,j.Name,qs.Id,qs.Status,s.Id,s.Status,s.StartedTime,s.SubmittedTime,s.CompletedTime,
              s.ErrorCode,s.ErrorMessage,r.OverallScore,r.Status,r.Summary,r.Strengths,r.Concerns,r.Recommendation,
              r.ResumeKeyInfo,r.VoiceInterviewInfo,r.DimensionScoresJson
            FROM dbo.InterviewInvitation i JOIN dbo.InterviewQuestionSet qs ON qs.Id=i.QuestionSetId
            JOIN dbo.CandidateJob cj ON cj.Id=i.CandidateJobId JOIN dbo.Candidate c ON c.Id=cj.CandidateId JOIN dbo.Job j ON j.Id=cj.JobId
            LEFT JOIN dbo.InterviewSession s ON s.Id=(SELECT TOP 1 x.Id FROM dbo.InterviewSession x WHERE x.InvitationId=i.Id ORDER BY x.RetakeNo DESC)
            LEFT JOIN dbo.InterviewReport r ON r.SessionId=s.Id WHERE i.Id=@Id AND i.IsDeleted=0;
            """;
        await using var connection=await connectionFactory.OpenAsync(ct);InterviewDetailDto? result;
        await using(var command=new SqlCommand(sql,connection)){command.Parameters.AddWithValue("@Id",invitationId);await using var reader=await command.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))return null;result=new(reader.GetInt64(0),reader.GetString(1),reader.GetDateTime(2),reader.GetString(3),N(reader,4),N(reader,5),reader.GetString(6),reader.GetInt64(7),reader.GetString(8),L(reader,9),N(reader,10),D(reader,11),D(reader,12),D(reader,13),N(reader,14),N(reader,15),M(reader,16),N(reader,17),N(reader,18),N(reader,19),N(reader,20),N(reader,21),N(reader,22),N(reader,23),N(reader,24),[],[]);}
        var deliveries=new List<InvitationDeliveryDto>();
        await using(var command=new SqlCommand("SELECT Channel,Status,MaskedDestination,ErrorCode,ErrorMessage FROM dbo.InterviewInvitationDelivery WHERE InvitationId=@Id ORDER BY Channel",connection)){command.Parameters.AddWithValue("@Id",invitationId);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))deliveries.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),N(reader,3),N(reader,4)));}
        result=result with{Deliveries=deliveries};
        if(result.SessionId is null)return result;var turns=new List<InterviewTurnDto>();
        await using(var command=new SqlCommand("SELECT Id,SequenceNo,AttemptNo,PromptText,Status,Transcript,TranscriptIsFinal,DurationMs,RowVersion FROM dbo.InterviewTurn WHERE SessionId=@Id ORDER BY SequenceNo,AttemptNo",connection)){command.Parameters.AddWithValue("@Id",result.SessionId.Value);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))turns.Add(new(reader.GetInt64(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetString(3),reader.GetString(4),N(reader,5),reader.GetBoolean(6),reader.IsDBNull(7)?null:reader.GetInt32(7),VersionString((byte[])reader[8])));}
        return result with{Turns=turns};
    }

    public async Task CorrectTranscriptAsync(long turnId, CorrectTranscriptRequest request, long userId, CancellationToken ct)
    {
        var corrected=Clean(request.CorrectedTranscript);var reason=Clean(request.Reason);if(corrected is null||corrected.Length>20000||reason is null||reason.Length>500)throw new BusinessException("TRANSCRIPT_CORRECTION_INVALID","修正文本和原因不能为空，且不能超过长度限制。");var version=ParseVersion(request.RowVersion);
        const string sql="""
            SET XACT_ABORT ON;BEGIN TRANSACTION;
            INSERT dbo.InterviewTranscriptCorrection(TurnId,PreviousTranscript,CorrectedTranscript,Reason,CorrectedBy)
            SELECT Id,Transcript,@Text,@Reason,@UserId FROM dbo.InterviewTurn WHERE Id=@Id AND RowVersion=@Version;
            IF @@ROWCOUNT=0 BEGIN ROLLBACK;RAISERROR(N'转写记录已变化。',16,1);RETURN;END;
            UPDATE dbo.InterviewTurn SET Transcript=@Text,TranscriptSource='MANUAL',TranscriptIsFinal=1,UpdateTime=SYSUTCDATETIME() WHERE Id=@Id;
            COMMIT;
            """;
        await using var connection=await connectionFactory.OpenAsync(ct);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@Id",turnId);command.Parameters.Add("@Version",SqlDbType.Timestamp).Value=version;command.Parameters.Add("@Text",SqlDbType.NVarChar,-1).Value=corrected;command.Parameters.Add("@Reason",SqlDbType.NVarChar,500).Value=reason;command.Parameters.AddWithValue("@UserId",userId);await command.ExecuteNonQueryAsync(ct);
    }

    public async Task SubmitReportAsync(long sessionId, long userId, CancellationToken ct)
    {
        await using var connection=await connectionFactory.OpenAsync(ct);
        const string headerSql="""
          SELECT s.Status,c.Name,j.Name,j.Requirement,p.Summary,p.HighestEducation,p.TotalExperienceMonths,p.CurrentCompany,p.CurrentTitle,
            cj.AIScore,cj.SkillScore,cj.ProjectScore,cj.ExperienceScore,cj.EducationScore,cj.Strengths,cj.Weaknesses,cj.Recommendation
          FROM dbo.InterviewSession s
          JOIN dbo.InterviewInvitation i ON i.Id=s.InvitationId AND i.IsDeleted=0
          JOIN dbo.InterviewQuestionSet qs ON qs.Id=s.QuestionSetId
          JOIN dbo.CandidateJob cj ON cj.Id=s.CandidateJobId
          JOIN dbo.Candidate c ON c.Id=cj.CandidateId AND c.IsDeleted=0
          JOIN dbo.Job j ON j.Id=cj.JobId AND j.IsDeleted=0
          LEFT JOIN dbo.CandidateProfile p ON p.CandidateId=c.Id AND p.ResumeId=qs.ResumeId
          WHERE s.Id=@Id;
          """;
        string previousStatus,candidateName,jobName;string? jobRequirement,profileSummary,highestEducation,currentCompany,currentTitle,strengths,weaknesses,previousRecommendation;int? experienceMonths;decimal? priorScore,skillScore,projectScore,experienceScore,educationScore;
        await using(var command=new SqlCommand(headerSql,connection)){command.Parameters.AddWithValue("@Id",sessionId);await using var reader=await command.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))throw new BusinessException("INTERVIEW_SESSION_NOT_FOUND","面试场次不存在或邀请已删除。",404);previousStatus=reader.GetString(0);candidateName=reader.GetString(1);jobName=reader.GetString(2);jobRequirement=N(reader,3);profileSummary=N(reader,4);highestEducation=N(reader,5);experienceMonths=reader.IsDBNull(6)?null:reader.GetInt32(6);currentCompany=N(reader,7);currentTitle=N(reader,8);priorScore=M(reader,9);skillScore=M(reader,10);projectScore=M(reader,11);experienceScore=M(reader,12);educationScore=M(reader,13);strengths=N(reader,14);weaknesses=N(reader,15);previousRecommendation=N(reader,16);}
        if(previousStatus is not ("SUBMITTED" or "COMPLETED" or "REVIEW_REQUIRED"))throw new BusinessException("INTERVIEW_REPORT_NOT_READY","候选人提交面试后才能生成报告。",409);

        var turns=new List<object>();var required=0;var completedRequired=0;var totalDurationMs=0;
        const string turnSql="""
          SELECT q.SequenceNo,q.QuestionType,q.DimensionCode,q.IsRequired,q.IsScored,t.PromptText,t.Transcript,t.TranscriptIsFinal,t.DurationMs
          FROM dbo.InterviewTurn t JOIN dbo.InterviewSessionQuestion q ON q.Id=t.QuestionId
          WHERE t.SessionId=@Id AND t.IsFinalAttempt=1 ORDER BY q.SequenceNo;
          """;
        await using(var command=new SqlCommand(turnSql,connection)){command.Parameters.AddWithValue("@Id",sessionId);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct)){var isRequired=reader.GetBoolean(3);var transcript=N(reader,6);var isFinal=reader.GetBoolean(7);var duration=reader.IsDBNull(8)?0:reader.GetInt32(8);if(isRequired){required++;if(isFinal&&!string.IsNullOrWhiteSpace(transcript))completedRequired++;}totalDurationMs+=duration;turns.Add(new{sequence_no=reader.GetInt32(0),question_type=reader.GetString(1),dimension_code=N(reader,2),is_required=isRequired,is_scored=reader.GetBoolean(4),question=reader.GetString(5),transcript,is_final=isFinal,duration_ms=duration});}}
        if(required==0||completedRequired<required)throw new BusinessException("INTERVIEW_ANSWERS_INCOMPLETE",$"必答题尚未全部完成（{completedRequired}/{required}）。",409);

        var work=new List<object>();
        await using(var command=new SqlCommand("SELECT TOP 5 w.CompanyName,w.JobTitle,w.StartDate,w.EndDate,w.Description,w.IsCurrent FROM dbo.CandidateWorkExperience w JOIN dbo.CandidateJob cj ON cj.CandidateId=w.CandidateId JOIN dbo.InterviewSession s ON s.CandidateJobId=cj.Id WHERE s.Id=@Id ORDER BY w.IsCurrent DESC,w.EndDate DESC,w.Id DESC",connection)){command.Parameters.AddWithValue("@Id",sessionId);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))work.Add(new{company=reader.GetString(0),title=N(reader,1),start=D(reader,2),end=D(reader,3),description=N(reader,4),current=reader.GetBoolean(5)});}
        var education=new List<object>();
        await using(var command=new SqlCommand("SELECT TOP 3 e.SchoolName,e.Major,e.Degree,e.StartDate,e.EndDate FROM dbo.CandidateEducation e JOIN dbo.CandidateJob cj ON cj.CandidateId=e.CandidateId JOIN dbo.InterviewSession s ON s.CandidateJobId=cj.Id WHERE s.Id=@Id ORDER BY e.EndDate DESC,e.Id DESC",connection)){command.Parameters.AddWithValue("@Id",sessionId);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))education.Add(new{school=reader.GetString(0),major=N(reader,1),degree=N(reader,2),start=D(reader,3),end=D(reader,4)});}

        await using(var mark=new SqlCommand("UPDATE dbo.InterviewSession SET Status='PROCESSING',ErrorCode=NULL,ErrorMessage=NULL,UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND Status=@Status",connection)){mark.Parameters.AddWithValue("@Id",sessionId);mark.Parameters.AddWithValue("@Status",previousStatus);if(await mark.ExecuteNonQueryAsync(ct)!=1)throw Conflict("面试状态已经变化，请刷新后重试。");}
        try
        {
            var context=new{candidate=new{name=candidateName,summary=profileSummary,highest_education=highestEducation,total_experience_months=experienceMonths,current_company=currentCompany,current_title=currentTitle,work_experiences=work,educations=education},job=new{name=jobName,requirement=jobRequirement},resume_match=new{overall_score=priorScore,skill_score=skillScore,project_score=projectScore,experience_score=experienceScore,education_score=educationScore,strengths,weaknesses,recommendation=previousRecommendation},voice_interview=new{required_questions=required,completed_required_questions=completedRequired,total_duration_ms=totalDurationMs,turns}};
            var report=await GenerateAiReportAsync(context,ct);
            var dimensionJson=JsonSerializer.Serialize(report.DimensionScores);var evidenceJson=JsonSerializer.Serialize(report.Evidence);var riskJson=JsonSerializer.Serialize(report.RiskFlags);var rawJson=JsonSerializer.Serialize(report);
            const string save="""
              MERGE dbo.InterviewReport AS target USING(SELECT @SessionId SessionId) source ON target.SessionId=source.SessionId
              WHEN MATCHED THEN UPDATE SET Status='SUCCESS',OverallScore=@Score,DimensionScoresJson=@Dimensions,EvidenceJson=@Evidence,Summary=@Summary,Strengths=@Strengths,Concerns=@Concerns,Recommendation=@Recommendation,RiskFlagsJson=@Risks,RawModelJson=@Raw,ModelName=@Model,PromptVersion=@Prompt,ResumeKeyInfo=@Resume,VoiceInterviewInfo=@Voice,UpdateTime=SYSUTCDATETIME()
              WHEN NOT MATCHED THEN INSERT(SessionId,Status,OverallScore,DimensionScoresJson,EvidenceJson,Summary,Strengths,Concerns,Recommendation,RiskFlagsJson,RawModelJson,ModelName,PromptVersion,ResumeKeyInfo,VoiceInterviewInfo)
                VALUES(@SessionId,'SUCCESS',@Score,@Dimensions,@Evidence,@Summary,@Strengths,@Concerns,@Recommendation,@Risks,@Raw,@Model,@Prompt,@Resume,@Voice);
              UPDATE dbo.InterviewSession SET Status='COMPLETED',CompletedTime=COALESCE(CompletedTime,SYSUTCDATETIME()),LlmProvider='DEEPSEEK',LlmModel=@Model,PromptVersion=@Prompt,ErrorCode=NULL,ErrorMessage=NULL,UpdateTime=SYSUTCDATETIME() WHERE Id=@SessionId AND Status='PROCESSING';
              INSERT dbo.InterviewAuditLog(ActorType,ActorUserId,ActionCode,EntityType,EntityId,SessionId,ResultCode,DetailJson)
              VALUES('HR',@UserId,'INTERVIEW_REPORT_SUBMIT','INTERVIEW_SESSION',@SessionId,@SessionId,'SUCCESS',N'{"reportGenerated":true}');
              """;
            await using var command=new SqlCommand(save,connection);command.Parameters.AddWithValue("@SessionId",sessionId);command.Parameters.AddWithValue("@UserId",userId);command.Parameters.AddWithValue("@Score",report.OverallScore);command.Parameters.Add("@Dimensions",SqlDbType.NVarChar,-1).Value=dimensionJson;command.Parameters.Add("@Evidence",SqlDbType.NVarChar,-1).Value=evidenceJson;command.Parameters.Add("@Summary",SqlDbType.NVarChar,-1).Value=report.Summary;command.Parameters.Add("@Strengths",SqlDbType.NVarChar,-1).Value=report.Strengths;command.Parameters.Add("@Concerns",SqlDbType.NVarChar,-1).Value=report.Concerns;command.Parameters.AddWithValue("@Recommendation",report.Recommendation);command.Parameters.Add("@Risks",SqlDbType.NVarChar,-1).Value=riskJson;command.Parameters.Add("@Raw",SqlDbType.NVarChar,-1).Value=rawJson;command.Parameters.AddWithValue("@Model",report.Model);command.Parameters.AddWithValue("@Prompt",report.PromptVersion);command.Parameters.Add("@Resume",SqlDbType.NVarChar,-1).Value=report.ResumeKeyInfo;command.Parameters.Add("@Voice",SqlDbType.NVarChar,-1).Value=report.VoiceInterviewInfo;await command.ExecuteNonQueryAsync(ct);
        }
        catch
        {
            await using var restore=new SqlCommand("UPDATE dbo.InterviewSession SET Status=@Status,ErrorCode='REPORT_GENERATION_FAILED',ErrorMessage=N'报告生成失败，请重试。',UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND Status='PROCESSING'",connection);restore.Parameters.AddWithValue("@Id",sessionId);restore.Parameters.AddWithValue("@Status",previousStatus);await restore.ExecuteNonQueryAsync(CancellationToken.None);throw;
        }
    }

    public async Task AbortSessionAsync(long sessionId, string reason, long userId, CancellationToken ct)
    {
        reason=Clean(reason)??"HR手动终止";if(reason.Length>1000)throw new BusinessException("ABORT_REASON_INVALID","终止原因不能超过1000字。");
        const string sql="UPDATE dbo.InterviewSession SET Status='ABORTED',ErrorCode='HR_ABORTED',ErrorMessage=@Reason,UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND Status IN('NOT_STARTED','DEVICE_CHECK','IN_PROGRESS','PAUSED','PROCESSING')";
        await using var connection=await connectionFactory.OpenAsync(ct);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@Id",sessionId);command.Parameters.Add("@Reason",SqlDbType.NVarChar,1000).Value=reason;if(await command.ExecuteNonQueryAsync(ct)!=1)throw Conflict("当前场次不能终止。");
    }

    private async Task<AiQuestionSetResponse> GenerateAiQuestionSetAsync(object context,CancellationToken ct)
    {
        using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(130)};
        try
        {
            using var response=await client.PostAsync(settings.AgentBaseUrl.TrimEnd('/')+"/interview/question-set/draft",new StringContent(JsonSerializer.Serialize(new{context}),Encoding.UTF8,"application/json"),ct);
            if(!response.IsSuccessStatusCode)throw new BusinessException("INTERVIEW_AI_FAILED",$"AI题集创建失败（HTTP {(int)response.StatusCode}），请确认 Python Agent 和 DeepSeek 可用。",503);
            var result=JsonSerializer.Deserialize<AiQuestionSetResponse>(await response.Content.ReadAsStringAsync(ct),new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if(result is null||string.IsNullOrWhiteSpace(result.Model)||string.IsNullOrWhiteSpace(result.PromptVersion)||result.Questions.Count is <1 or >100)throw new BusinessException("INTERVIEW_AI_INVALID_RESPONSE","AI未返回有效题集。",503);
            foreach(var q in result.Questions)
            {
                q.QuestionType=q.QuestionType.Trim().ToUpperInvariant();q.QuestionText=q.QuestionText.Trim();q.DimensionCode=Clean(q.DimensionCode);q.GenerationReason=Clean(q.GenerationReason);
                if(!SetTypes.Contains(q.QuestionType)||q.QuestionText.Length is <1 or >2000||q.MaxAnswerSeconds is <5 or >1800)throw new BusinessException("INTERVIEW_AI_INVALID_RESPONSE","AI返回了不符合面试规则的题目。",503);
            }
            return result;
        }
        catch(HttpRequestException){throw new BusinessException("AGENT_OFFLINE","Python Agent服务未启动，无法创建候选人题集。",503);}
        catch(TaskCanceledException) when(!ct.IsCancellationRequested){throw new BusinessException("INTERVIEW_AI_TIMEOUT","AI创建题集超时，请稍后重试。",503);}
    }

    private async Task<AiInterviewReportResponse> GenerateAiReportAsync(object context,CancellationToken ct)
    {
        using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(130)};
        try
        {
            using var response=await client.PostAsync(settings.AgentBaseUrl.TrimEnd('/')+"/interview/report/generate",new StringContent(JsonSerializer.Serialize(new{context}),Encoding.UTF8,"application/json"),ct);
            if(!response.IsSuccessStatusCode)throw new BusinessException("INTERVIEW_REPORT_AI_FAILED",$"AI报告生成失败（HTTP {(int)response.StatusCode}），请确认 Python Agent 和 DeepSeek 可用。",503);
            var result=JsonSerializer.Deserialize<AiInterviewReportResponse>(await response.Content.ReadAsStringAsync(ct),new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if(result is null||result.OverallScore is <0 or >100||string.IsNullOrWhiteSpace(result.ResumeKeyInfo)||string.IsNullOrWhiteSpace(result.VoiceInterviewInfo)||string.IsNullOrWhiteSpace(result.Summary)||string.IsNullOrWhiteSpace(result.Strengths)||string.IsNullOrWhiteSpace(result.Concerns)||!new[]{"PROCEED","HOLD","NOT_RECOMMENDED","INSUFFICIENT_DATA"}.Contains(result.Recommendation)||result.DimensionScores.Count==0||result.DimensionScores.Values.Any(x=>x is <0 or >100)||string.IsNullOrWhiteSpace(result.Model)||string.IsNullOrWhiteSpace(result.PromptVersion))throw new BusinessException("INTERVIEW_REPORT_AI_INVALID","AI未返回有效面试报告。",503);
            return result;
        }
        catch(HttpRequestException){throw new BusinessException("AGENT_OFFLINE","Python Agent服务未启动，无法生成面试报告。",503);}
        catch(TaskCanceledException) when(!ct.IsCancellationRequested){throw new BusinessException("INTERVIEW_REPORT_AI_TIMEOUT","AI生成面试报告超时，请稍后重试。",503);}
    }

    private static InterviewPlanDto MapPlan(SqlDataReader r,IReadOnlyList<InterviewQuestionInput> questions)=>new(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetInt32(4),r.GetString(5),r.GetInt32(6),r.GetInt32(7),r.GetString(8),r.GetDateTime(9),VersionString((byte[])r[10]),questions);
    private static async Task ReplacePlanQuestionsAsync(SqlConnection c,SqlTransaction t,long id,IReadOnlyList<InterviewQuestionInput> questions,CancellationToken ct){await using(var d=new SqlCommand("DELETE dbo.InterviewQuestion WHERE PlanId=@Id",c,t)){d.Parameters.AddWithValue("@Id",id);await d.ExecuteNonQueryAsync(ct);}foreach(var q in questions){await using var x=new SqlCommand("INSERT dbo.InterviewQuestion(PlanId,SequenceNo,QuestionType,QuestionText,DimensionCode,MaxAnswerSeconds,MinTranscriptChars,IsScored,IsRequired) VALUES(@Id,@Seq,@Type,@Text,@Dimension,@Seconds,@Chars,@Scored,@Required)",c,t);x.Parameters.AddWithValue("@Id",id);x.Parameters.AddWithValue("@Seq",q.SequenceNo);x.Parameters.AddWithValue("@Type",q.QuestionType.Trim().ToUpperInvariant());x.Parameters.Add("@Text",SqlDbType.NVarChar,2000).Value=q.QuestionText.Trim();x.Parameters.Add("@Dimension",SqlDbType.VarChar,50).Value=(object?)Clean(q.DimensionCode)??DBNull.Value;x.Parameters.AddWithValue("@Seconds",q.MaxAnswerSeconds);x.Parameters.AddWithValue("@Chars",q.MinTranscriptChars);x.Parameters.AddWithValue("@Scored",q.IsScored);x.Parameters.AddWithValue("@Required",q.IsRequired);await x.ExecuteNonQueryAsync(ct);}}
    private static void ValidatePlan(SaveInterviewPlanRequest r){if(r.JobId<=0||string.IsNullOrWhiteSpace(r.Name)||r.Name.Trim().Length>200||r.DurationMinutes is <1 or >120||r.MaxSessionMinutes<r.DurationMinutes||r.MaxSessionMinutes>180||string.IsNullOrWhiteSpace(r.ConsentTextVersion)||r.ConsentTextVersion.Length>50)throw new BusinessException("INTERVIEW_PLAN_INVALID","方案基本信息无效。");if(r.Questions.Count is <1 or >100||r.Questions.Select(x=>x.SequenceNo).Distinct().Count()!=r.Questions.Count||r.Questions.Any(x=>x.SequenceNo<=0||!PlanTypes.Contains(x.QuestionType.Trim().ToUpperInvariant())||string.IsNullOrWhiteSpace(x.QuestionText)||x.QuestionText.Trim().Length>2000||x.MaxAnswerSeconds is <5 or >1800||x.MinTranscriptChars is <0 or >4000))throw new BusinessException("INTERVIEW_QUESTION_INVALID","方案题目无效或序号重复。");}
    private static void ValidateSetQuestions(IReadOnlyList<QuestionSetQuestionDto> q){if(q.Count is <1 or >100||q.Select(x=>x.SequenceNo).Distinct().Count()!=q.Count||q.Any(x=>x.SequenceNo<=0||!SetTypes.Contains(x.QuestionType.Trim().ToUpperInvariant())||string.IsNullOrWhiteSpace(x.QuestionText)||x.QuestionText.Trim().Length>2000||x.MaxAnswerSeconds is <5 or >1800))throw new BusinessException("QUESTION_SET_INVALID","题集题目无效或序号重复。");}
    private static void AddPlanParameters(SqlCommand c,SaveInterviewPlanRequest r){c.Parameters.AddWithValue("@JobId",r.JobId);c.Parameters.Add("@Name",SqlDbType.NVarChar,200).Value=r.Name.Trim();c.Parameters.AddWithValue("@Duration",r.DurationMinutes);c.Parameters.AddWithValue("@MaxDuration",r.MaxSessionMinutes);c.Parameters.Add("@Consent",SqlDbType.VarChar,50).Value=r.ConsentTextVersion.Trim();}
    private static void AddSetQuestionParameters(SqlCommand c,long id,QuestionSetQuestionDto q){c.Parameters.AddWithValue("@Id",id);c.Parameters.AddWithValue("@Seq",q.SequenceNo);c.Parameters.AddWithValue("@Type",q.QuestionType.Trim().ToUpperInvariant());c.Parameters.Add("@Text",SqlDbType.NVarChar,2000).Value=q.QuestionText.Trim();c.Parameters.Add("@Dimension",SqlDbType.VarChar,50).Value=(object?)Clean(q.DimensionCode)??DBNull.Value;c.Parameters.Add("@Reason",SqlDbType.NVarChar,1000).Value=(object?)Clean(q.GenerationReason)??DBNull.Value;c.Parameters.Add("@Evidence",SqlDbType.NVarChar,-1).Value=(object?)Clean(q.ExpectedEvidenceJson)??DBNull.Value;c.Parameters.AddWithValue("@Seconds",q.MaxAnswerSeconds);c.Parameters.AddWithValue("@Required",q.IsRequired);c.Parameters.AddWithValue("@Scored",q.IsScored);}
    private static byte[] ParseVersion(string? value){try{return string.IsNullOrWhiteSpace(value)?throw new FormatException():Convert.FromBase64String(value);}catch{throw new BusinessException("ROW_VERSION_INVALID","数据版本无效，请刷新页面。");}}
    private static string VersionString(byte[] value)=>Convert.ToBase64String(value);
    private static string? Clean(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static void AddNullable(SqlCommand c,string name,SqlDbType type,int size,string? value)=>c.Parameters.Add(name,type,size).Value=(object?)value??DBNull.Value;
    private static string Sha256Hex(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static byte[] Combine(byte[] a,byte[] b){var result=new byte[a.Length+b.Length];Buffer.BlockCopy(a,0,result,0,a.Length);Buffer.BlockCopy(b,0,result,a.Length,b.Length);return result;}
    private static string GenerateCode(){const string alphabet="23456789ABCDEFGHJKLMNPQRSTUVWXYZ";var bytes=RandomNumberGenerator.GetBytes(8);return string.Concat(bytes.Select(x=>alphabet[x%alphabet.Length]));}
    private static BusinessException Conflict(string message)=>new("CONCURRENCY_CONFLICT",message,409);
    private static string? N(SqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);private static long? L(SqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetInt64(i);private static DateTime? D(SqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetDateTime(i);private static decimal? M(SqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetDecimal(i);

    private sealed record AiQuestionSetResponse(
        [property:JsonPropertyName("questions")] List<AiQuestion> Questions,
        [property:JsonPropertyName("model")] string Model,
        [property:JsonPropertyName("prompt_version")] string PromptVersion);
    private sealed record AiInterviewReportResponse(
        [property:JsonPropertyName("resume_key_info")] string ResumeKeyInfo,
        [property:JsonPropertyName("voice_interview_info")] string VoiceInterviewInfo,
        [property:JsonPropertyName("overall_score")] decimal OverallScore,
        [property:JsonPropertyName("dimension_scores")] Dictionary<string,decimal> DimensionScores,
        [property:JsonPropertyName("summary")] string Summary,
        [property:JsonPropertyName("strengths")] string Strengths,
        [property:JsonPropertyName("concerns")] string Concerns,
        [property:JsonPropertyName("recommendation")] string Recommendation,
        [property:JsonPropertyName("evidence")] JsonElement Evidence,
        [property:JsonPropertyName("risk_flags")] JsonElement RiskFlags,
        [property:JsonPropertyName("model")] string Model,
        [property:JsonPropertyName("prompt_version")] string PromptVersion);
    private sealed class AiQuestion
    {
        [JsonPropertyName("question_type")] public string QuestionType {get;set;}="";
        [JsonPropertyName("question_text")] public string QuestionText {get;set;}="";
        [JsonPropertyName("dimension_code")] public string? DimensionCode {get;set;}
        [JsonPropertyName("generation_reason")] public string? GenerationReason {get;set;}
        [JsonPropertyName("expected_evidence_json")] public string? ExpectedEvidenceJson {get;set;}
        [JsonPropertyName("max_answer_seconds")] public int MaxAnswerSeconds {get;set;}=120;
        [JsonPropertyName("is_required")] public bool IsRequired {get;set;}=true;
        [JsonPropertyName("is_scored")] public bool IsScored {get;set;}=true;
    }
}

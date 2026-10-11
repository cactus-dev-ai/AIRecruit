using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RecruitAI.Application.Common;
using RecruitAI.Application.Interviews;
using RecruitAI.Infrastructure.Database;

namespace RecruitAI.Infrastructure.Interviews;

public sealed class SqlPublicInterviewService(
    SqlConnectionFactory connectionFactory,
    InterviewManagementSettings managementSettings,
    TencentInterviewGateway tencent) : IPublicInterviewService
{
    private static readonly Regex PhonePattern = new("^1[3-9]\\d{9}$", RegexOptions.Compiled);
    private const int MaxAudioBytes = 3 * 1024 * 1024;

    public async Task<VerifiedInterviewDto> VerifyAsync(VerifyInterviewCodeRequest request, string? ip, string? clientInfo, CancellationToken ct)
    {
        var phone = NormalizePhone(request.Phone);
        var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (!PhonePattern.IsMatch(phone)) throw Bad("PHONE_INVALID", "请输入有效的中国大陆手机号码。");
        if (code.Length != 8) throw Bad("CODE_INVALID", "请输入 8 位面试邀请码。");

        var lookup = HMACSHA256.HashData(Encoding.UTF8.GetBytes(managementSettings.FallbackCodeLookupKey), Encoding.UTF8.GetBytes(code));
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        const string findSql = """
            SELECT TOP(1) i.Id,i.FallbackCodeVerifier,i.FallbackCodeSalt,i.FallbackCodeFailedCount,i.Status,i.ExpireTime,
              c.Name,c.PhoneNormalized,j.Name
            FROM dbo.InterviewInvitation i WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.CandidateJob cj ON cj.Id=i.CandidateJobId
            JOIN dbo.Candidate c ON c.Id=cj.CandidateId
            JOIN dbo.Job j ON j.Id=cj.JobId
            WHERE i.FallbackCodeLookupHash=@Lookup AND i.IsDeleted=0
            ORDER BY i.Id DESC;
            """;
        long invitationId; byte[] verifier; byte[] salt; int failures; string status; DateTime expiry; string candidateName; string? expectedPhone; string jobName;
        await using (var command = new SqlCommand(findSql, connection, transaction))
        {
            command.Parameters.Add("@Lookup", SqlDbType.Binary, 32).Value = lookup;
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw Bad("CODE_INVALID", "手机号或邀请码不正确。");
            invitationId = reader.GetInt64(0); verifier = (byte[])reader[1]; salt = (byte[])reader[2]; failures = reader.GetInt32(3);
            status = reader.GetString(4); expiry = reader.GetDateTime(5); candidateName = reader.GetString(6);
            expectedPhone = reader.IsDBNull(7) ? null : reader.GetString(7); jobName = reader.GetString(8);
        }
        if (status == "REVOKED") throw Bad("INVITATION_REVOKED", "该邀请已被撤销。", 410);
        if (expiry <= DateTime.UtcNow) throw Bad("INVITATION_EXPIRED", "该邀请已过期，请联系招聘人员。", 410);
        if (failures >= 5) throw Bad("CODE_LOCKED", "验证码尝试次数过多，请联系招聘人员。", 423);

        var actual = SHA256.HashData(Combine(salt, Encoding.UTF8.GetBytes(code)));
        var phoneMatches = string.IsNullOrWhiteSpace(expectedPhone) || NormalizePhone(expectedPhone) == phone;
        if (!CryptographicOperations.FixedTimeEquals(actual, verifier) || !phoneMatches)
        {
            await ExecuteAsync(connection, transaction, "UPDATE dbo.InterviewInvitation SET FallbackCodeFailedCount=FallbackCodeFailedCount+1,FallbackCodeLastFailedTime=SYSUTCDATETIME(),FallbackCodeLockedTime=CASE WHEN FallbackCodeFailedCount+1>=5 THEN SYSUTCDATETIME() ELSE NULL END,UpdateTime=SYSUTCDATETIME() WHERE Id=@Id", ct, ("@Id", invitationId));
            await transaction.CommitAsync(ct);
            throw Bad("VERIFY_FAILED", "手机号或邀请码不正确。");
        }

        var now = DateTime.UtcNow;
        var identityHash = SHA256.HashData(Encoding.UTF8.GetBytes("H5_DEBUG:" + phone));
        var phoneHash = SHA256.HashData(Encoding.UTF8.GetBytes(phone));
        var clientJson = string.IsNullOrWhiteSpace(clientInfo) ? null : clientInfo[..Math.Min(clientInfo.Length, 1000)];
        const string bindSql = """
            UPDATE dbo.InterviewInvitation SET Status='BOUND',VerificationMethod='FALLBACK_CODE',
              NotifiedTime=COALESCE(NotifiedTime,SYSUTCDATETIME()),OpenedTime=COALESCE(OpenedTime,SYSUTCDATETIME()),
              VerifiedTime=COALESCE(VerifiedTime,SYSUTCDATETIME()),BoundTime=COALESCE(BoundTime,SYSUTCDATETIME()),
              FallbackCodeUsedTime=COALESCE(FallbackCodeUsedTime,SYSUTCDATETIME()),UpdateTime=SYSUTCDATETIME() WHERE Id=@Id;
            MERGE dbo.InterviewWechatIdentity AS target
            USING(SELECT @Id InvitationId) source ON target.InvitationId=source.InvitationId
            WHEN MATCHED THEN UPDATE SET OpenIdHash=@IdentityHash,PhoneHash=@PhoneHash,PhoneLast4=@Last4,
              VerificationMethod='FALLBACK_CODE',PhoneMatchStatus=@Match,VerifiedTime=SYSUTCDATETIME(),LastLoginTime=SYSUTCDATETIME(),ClientInfoJson=@Client,UpdateTime=SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT(InvitationId,MiniProgramAppId,OpenIdHash,PhoneHash,PhoneLast4,VerificationMethod,PhoneMatchStatus,VerifiedTime,LastLoginTime,ClientInfoJson)
              VALUES(@Id,'H5_DEBUG',@IdentityHash,@PhoneHash,@Last4,'FALLBACK_CODE',@Match,SYSUTCDATETIME(),SYSUTCDATETIME(),@Client);
            SELECT Id FROM dbo.InterviewWechatIdentity WHERE InvitationId=@Id;
            """;
        long identityId;
        await using (var command = new SqlCommand(bindSql, connection, transaction))
        {
            command.Parameters.AddWithValue("@Id", invitationId);
            command.Parameters.Add("@IdentityHash", SqlDbType.Binary, 32).Value = identityHash;
            command.Parameters.Add("@PhoneHash", SqlDbType.Binary, 32).Value = phoneHash;
            command.Parameters.Add("@Last4", SqlDbType.Char, 4).Value = phone[^4..];
            command.Parameters.AddWithValue("@Match", expectedPhone is null ? "NOT_AVAILABLE" : "MATCHED");
            command.Parameters.Add("@Client", SqlDbType.NVarChar, -1).Value = (object?)clientJson ?? DBNull.Value;
            identityId = Convert.ToInt64(await command.ExecuteScalarAsync(ct));
        }

        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var accessToken = Base64Url(tokenBytes);
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(accessToken));
        var expires = expiry < now.AddHours(2) ? expiry : now.AddHours(2);
        const string tokenSql = """
            INSERT dbo.InterviewAccessSession(TokenHash,MiniProgramAppId,OpenIdHash,WechatIdentityId,InvitationId,Status,ExpiresTime,BoundTime,IpHash,ClientInfoJson)
            VALUES(@TokenHash,'H5_DEBUG',@IdentityHash,@IdentityId,@InvitationId,'BOUND',@Expires,SYSUTCDATETIME(),@IpHash,@Client);
            """;
        await using (var command = new SqlCommand(tokenSql, connection, transaction))
        {
            command.Parameters.Add("@TokenHash", SqlDbType.Binary, 32).Value = tokenHash;
            command.Parameters.Add("@IdentityHash", SqlDbType.Binary, 32).Value = identityHash;
            command.Parameters.AddWithValue("@IdentityId", identityId); command.Parameters.AddWithValue("@InvitationId", invitationId);
            command.Parameters.AddWithValue("@Expires", expires);
            command.Parameters.Add("@IpHash", SqlDbType.Binary, 32).Value = string.IsNullOrWhiteSpace(ip) ? DBNull.Value : SHA256.HashData(Encoding.UTF8.GetBytes(ip));
            command.Parameters.Add("@Client", SqlDbType.NVarChar, -1).Value = (object?)clientJson ?? DBNull.Value;
            await command.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return new(accessToken, expires, candidateName, jobName);
    }

    public async Task<PublicInterviewRoomDto> StartAsync(string accessToken, StartPublicInterviewRequest request, string? ip, CancellationToken ct)
    {
        if (!request.ConsentGranted) throw Bad("CONSENT_REQUIRED", "需要同意录音及 AI 处理说明后才能开始面试。");
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var access = await GetAccessAsync(connection, transaction, accessToken, ct);
        const string inviteSql = """
            SELECT i.CandidateJobId,i.PlanId,i.QuestionSetId,qs.QuestionSetHash,p.ConsentTextVersion
            FROM dbo.InterviewInvitation i JOIN dbo.InterviewQuestionSet qs ON qs.Id=i.QuestionSetId JOIN dbo.InterviewPlan p ON p.Id=i.PlanId
            WHERE i.Id=@Id AND i.IsDeleted=0 AND i.Status='BOUND' AND i.ExpireTime>SYSUTCDATETIME() AND qs.Status='CONFIRMED';
            """;
        long candidateJobId, planId, questionSetId; string setHash, consentVersion;
        await using (var command = new SqlCommand(inviteSql, connection, transaction))
        {
            command.Parameters.AddWithValue("@Id", access.InvitationId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw Bad("INVITATION_NOT_READY", "面试邀请不可用或题集尚未发布。", 409);
            candidateJobId=reader.GetInt64(0); planId=reader.GetInt64(1); questionSetId=reader.GetInt64(2); setHash=reader.GetString(3); consentVersion=reader.GetString(4);
        }
        const string sessionSql = """
            DECLARE @SessionId BIGINT=(SELECT TOP(1) Id FROM dbo.InterviewSession WITH(UPDLOCK,HOLDLOCK) WHERE InvitationId=@InvitationId ORDER BY RetakeNo DESC);
            IF @SessionId IS NULL BEGIN
              INSERT dbo.InterviewSession(InvitationId,CandidateJobId,PlanId,QuestionSetId,Status,StartedTime,LastHeartbeatTime,ClientInfoJson,DeviceCheckJson,AsrProvider,LlmProvider,AsrModel,PromptVersion,QuestionSetHash)
              VALUES(@InvitationId,@CandidateJobId,@PlanId,@QuestionSetId,'IN_PROGRESS',SYSUTCDATETIME(),SYSUTCDATETIME(),@Client,@Device,'TENCENT_ASR','LOCAL_RULES',@AsrModel,'h5-debug-v1',@SetHash);
              SET @SessionId=SCOPE_IDENTITY();
              INSERT dbo.InterviewTurn(SessionId,QuestionId,SequenceNo,PromptText,Status)
                SELECT @SessionId,q.Id,q.SequenceNo,q.QuestionText,'CREATED' FROM dbo.InterviewSessionQuestion q WHERE q.QuestionSetId=@QuestionSetId AND q.Status='CONFIRMED' ORDER BY q.SequenceNo;
            END ELSE UPDATE dbo.InterviewSession SET Status=CASE WHEN Status IN('NOT_STARTED','DEVICE_CHECK','PAUSED') THEN 'IN_PROGRESS' ELSE Status END,StartedTime=COALESCE(StartedTime,SYSUTCDATETIME()),LastHeartbeatTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME() WHERE Id=@SessionId;
            SELECT @SessionId;
            """;
        long sessionId;
        await using (var command = new SqlCommand(sessionSql, connection, transaction))
        {
            command.Parameters.AddWithValue("@InvitationId", access.InvitationId); command.Parameters.AddWithValue("@CandidateJobId",candidateJobId);
            command.Parameters.AddWithValue("@PlanId",planId); command.Parameters.AddWithValue("@QuestionSetId",questionSetId);
            command.Parameters.AddWithValue("@SetHash",setHash); command.Parameters.AddWithValue("@AsrModel","16k_zh");
            command.Parameters.Add("@Client",SqlDbType.NVarChar,-1).Value=(object?)request.ClientInfo??DBNull.Value;
            command.Parameters.Add("@Device",SqlDbType.NVarChar,-1).Value=(object?)request.DeviceCheckJson??DBNull.Value;
            sessionId=Convert.ToInt64(await command.ExecuteScalarAsync(ct));
        }
        foreach (var consentType in new[] { "AI_INTERVIEW", "AUDIO_RECORDING", "THIRD_PARTY_PROCESSING" })
            await ExecuteAsync(connection, transaction, "IF NOT EXISTS(SELECT 1 FROM dbo.InterviewConsent WHERE SessionId=@SessionId AND ConsentType=@Type AND TextVersion=@Version) INSERT dbo.InterviewConsent(SessionId,ConsentType,TextVersion,Granted,GrantedTime,IpHash,ClientInfo) VALUES(@SessionId,@Type,@Version,1,SYSUTCDATETIME(),@IpHash,@Client)", ct,
                ("@SessionId",sessionId),("@Type",consentType),("@Version",consentVersion),("@IpHash",string.IsNullOrWhiteSpace(ip)?DBNull.Value:SHA256.HashData(Encoding.UTF8.GetBytes(ip))),("@Client",(object?)request.ClientInfo??DBNull.Value));

        var roomId = $"h5-{sessionId}"; var userId = $"candidate-{sessionId}"; var robotId = $"recorder-{sessionId}";
        const string rtcSql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.InterviewRtcTask WHERE SessionId=@SessionId)
              INSERT dbo.InterviewRtcTask(SessionId,SdkAppId,RoomId,CandidateUserId,RobotUserId,ProviderSessionId,Status,StopDeadlineTime)
              VALUES(@SessionId,@SdkAppId,@RoomId,@UserId,@RobotId,@ProviderSession,'STARTING',DATEADD(MINUTE,60,SYSUTCDATETIME()));
            """;
        await ExecuteAsync(connection,transaction,rtcSql,ct,("@SessionId",sessionId),("@SdkAppId",tencent.SdkAppId),("@RoomId",roomId),("@UserId",userId),("@RobotId",robotId),("@ProviderSession",Guid.NewGuid().ToString("N")));
        var questions = new List<PublicInterviewQuestionDto>();
        await using (var command = new SqlCommand("SELECT t.Id,t.SequenceNo,t.PromptText,q.MaxAnswerSeconds,q.IsRequired FROM dbo.InterviewTurn t JOIN dbo.InterviewSessionQuestion q ON q.Id=t.QuestionId WHERE t.SessionId=@Id ORDER BY t.SequenceNo",connection,transaction))
        {
            command.Parameters.AddWithValue("@Id",sessionId); await using var reader=await command.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct)) questions.Add(new(reader.GetInt64(0),reader.GetInt32(1),reader.GetString(2),Math.Min(50,reader.GetInt32(3)),reader.GetBoolean(4)));
        }
        await transaction.CommitAsync(ct);
        return new(sessionId,"IN_PROGRESS",tencent.SdkAppId,roomId,userId,tencent.CreateUserSig(userId),questions);
    }

    public async Task BeginRecordingAsync(string accessToken, CancellationToken ct)
    {
        await using var connection=await connectionFactory.OpenAsync(ct);
        var access=await GetAccessAsync(connection,null,accessToken,ct);
        const string sql="SELECT r.Id,r.RoomId,r.RobotUserId,r.Status,r.ProviderTaskId FROM dbo.InterviewRtcTask r JOIN dbo.InterviewSession s ON s.Id=r.SessionId WHERE s.InvitationId=@InvitationId";
        long rtcId; string room,robot,status; string? existing;
        await using(var command=new SqlCommand(sql,connection)){command.Parameters.AddWithValue("@InvitationId",access.InvitationId);await using var reader=await command.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))throw Bad("SESSION_NOT_STARTED","面试尚未开始。",409);rtcId=reader.GetInt64(0);room=reader.GetString(1);robot=reader.GetString(2);status=reader.GetString(3);existing=reader.IsDBNull(4)?null:reader.GetString(4);}
        if(status=="RUNNING"&&!string.IsNullOrWhiteSpace(existing))return;
        var result=await tencent.StartRecordingAsync(room,robot,tencent.CreateUserSig(robot),ct);
        await using var update=new SqlCommand("UPDATE dbo.InterviewRtcTask SET ProviderTaskId=@TaskId,Status='RUNNING',StartedTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME() WHERE Id=@Id",connection);
        update.Parameters.AddWithValue("@TaskId",result.TaskId);update.Parameters.AddWithValue("@Id",rtcId);await update.ExecuteNonQueryAsync(ct);
    }

    public async Task<PublicTurnResultDto> SubmitAnswerAsync(string accessToken,long turnId,Stream audio,long length,string contentType,int durationMs,CancellationToken ct)
    {
        if(length<=44||length>MaxAudioBytes)throw Bad("AUDIO_SIZE_INVALID","单题录音必须小于 3MB。",413);
        if(durationMs<300||durationMs>60000)throw Bad("AUDIO_DURATION_INVALID","单题录音时长必须在 0.3 至 60 秒之间。");
        using var buffer=new MemoryStream();await audio.CopyToAsync(buffer,ct);var wav=buffer.ToArray();
        if(wav.Length<44||Encoding.ASCII.GetString(wav,0,4)!="RIFF"||Encoding.ASCII.GetString(wav,8,4)!="WAVE")throw Bad("AUDIO_FORMAT_INVALID","调试录音必须是 WAV 格式。");
        await using var connection=await connectionFactory.OpenAsync(ct);var access=await GetAccessAsync(connection,null,accessToken,ct);
        long sessionId;int sequence;
        await using(var command=new SqlCommand("SELECT t.SessionId,t.SequenceNo FROM dbo.InterviewTurn t JOIN dbo.InterviewSession s ON s.Id=t.SessionId WHERE t.Id=@TurnId AND s.InvitationId=@InvitationId AND s.Status='IN_PROGRESS'",connection)){command.Parameters.AddWithValue("@TurnId",turnId);command.Parameters.AddWithValue("@InvitationId",access.InvitationId);await using var reader=await command.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))throw Bad("TURN_NOT_AVAILABLE","当前题目不可提交。",409);sessionId=reader.GetInt64(0);sequence=reader.GetInt32(1);}
        var objectKey=$"interviews/{sessionId}/turn-{sequence}-{DateTime.UtcNow:yyyyMMddHHmmss}.wav";
        var started=DateTime.UtcNow;string transcript;
        try{transcript=await tencent.UploadAndRecognizeAsync(objectKey,wav,ct);}catch(Exception ex){await MarkTurnFailed(connection,turnId,ex.Message,ct);throw new BusinessException("ASR_FAILED","语音识别失败，请重新录制。",502);}
        if(string.IsNullOrWhiteSpace(transcript)){await MarkTurnFailed(connection,turnId,"empty transcript",ct);throw new BusinessException("ASR_EMPTY","没有识别到清晰语音，请重新录制。",422);}
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(ct);
        const string save="""
          INSERT dbo.InterviewAsset(SessionId,AssetType,StorageProvider,ObjectKey,RecordingMode,DeliveryStatus,MimeType,FileExtension,FileSize,Sha256,DurationMs,SampleRate,Channels,RetainUntil)
          VALUES(@SessionId,'CANDIDATE_RECORDING','TENCENT_COS',@ObjectKey,'CANDIDATE_SINGLE','SUCCESS','audio/wav','wav',@Size,@Sha,@Duration,16000,1,DATEADD(DAY,30,SYSUTCDATETIME()));
          DECLARE @AssetId BIGINT=SCOPE_IDENTITY();
          UPDATE dbo.InterviewTurn SET Status='COMPLETED',AudioAssetId=@AssetId,Transcript=@Transcript,TranscriptSource='ASR',TranscriptIsFinal=1,IsFinalAttempt=1,StartedTime=COALESCE(StartedTime,SYSUTCDATETIME()),EndedTime=SYSUTCDATETIME(),DurationMs=@Duration,AsrLatencyMs=@Latency,ErrorCode=NULL,ErrorMessage=NULL,UpdateTime=SYSUTCDATETIME() WHERE Id=@TurnId;
          UPDATE dbo.InterviewSession SET CurrentSequenceNo=CASE WHEN CurrentSequenceNo<@Sequence THEN @Sequence ELSE CurrentSequenceNo END,LastHeartbeatTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME() WHERE Id=@SessionId;
          """;
        await ExecuteAsync(connection,transaction,save,ct,("@SessionId",sessionId),("@ObjectKey",objectKey),("@Size",wav.Length),("@Sha",SHA256.HashData(wav)),("@Duration",durationMs),("@Transcript",transcript),("@Latency",(int)(DateTime.UtcNow-started).TotalMilliseconds),("@TurnId",turnId),("@Sequence",sequence));
        await transaction.CommitAsync(ct);return new(turnId,"COMPLETED",transcript,durationMs);
    }

    public async Task<PublicInterviewReportDto> SubmitAsync(string accessToken,CancellationToken ct)
    {
        await using var connection=await connectionFactory.OpenAsync(ct);var access=await GetAccessAsync(connection,null,accessToken,ct);
        long sessionId;string? taskId=null,room=null,robot=null;int required,completed;
        const string query="""
          SELECT s.Id,(SELECT COUNT(*) FROM dbo.InterviewTurn t JOIN dbo.InterviewSessionQuestion q ON q.Id=t.QuestionId WHERE t.SessionId=s.Id AND q.IsRequired=1),
            (SELECT COUNT(*) FROM dbo.InterviewTurn t JOIN dbo.InterviewSessionQuestion q ON q.Id=t.QuestionId WHERE t.SessionId=s.Id AND q.IsRequired=1 AND t.Status='COMPLETED'),r.ProviderTaskId,r.RoomId,r.RobotUserId
          FROM dbo.InterviewSession s LEFT JOIN dbo.InterviewRtcTask r ON r.SessionId=s.Id WHERE s.InvitationId=@InvitationId;
          """;
        await using(var command=new SqlCommand(query,connection)){command.Parameters.AddWithValue("@InvitationId",access.InvitationId);await using var reader=await command.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))throw Bad("SESSION_NOT_STARTED","面试尚未开始。",409);sessionId=reader.GetInt64(0);required=reader.GetInt32(1);completed=reader.GetInt32(2);taskId=reader.IsDBNull(3)?null:reader.GetString(3);room=reader.IsDBNull(4)?null:reader.GetString(4);robot=reader.IsDBNull(5)?null:reader.GetString(5);}
        if(completed<required)throw Bad("REQUIRED_ANSWERS_MISSING",$"还有 {required-completed} 道必答题未完成。",409);
        if(taskId is not null&&room is not null&&robot is not null){try{await tencent.StopRecordingAsync(room,robot,tencent.CreateUserSig(robot),taskId,ct);}catch{/* report remains available when recorder already stopped */}}
        var transcripts=new List<string>();await using(var command=new SqlCommand("SELECT Transcript FROM dbo.InterviewTurn WHERE SessionId=@Id AND TranscriptIsFinal=1 ORDER BY SequenceNo",connection)){command.Parameters.AddWithValue("@Id",sessionId);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))transcripts.Add(reader.GetString(0));}
        var totalChars=transcripts.Sum(x=>x.Length);var status=totalChars>=50?"SUCCESS":"REVIEW_REQUIRED";var recommendation=totalChars>=50?"HOLD":"INSUFFICIENT_DATA";
        var summary=$"候选人已完成 {completed} 道必答题，语音转写共 {totalChars} 个字符。该调试报告用于验证采集、转写和报告链路，需由 HR 结合录音与岗位标准复核。";
        var strengths=totalChars>=100?"回答信息量较充分，可进入人工证据核验。":"已完成规定的语音作答流程。";
        var concerns=totalChars<50?"有效转写内容较少，暂不足以形成自动判断。":"当前版本尚未启用岗位维度的 AI 自动评分。";
        const string save="""
          MERGE dbo.InterviewReport AS target USING(SELECT @SessionId SessionId) source ON target.SessionId=source.SessionId
          WHEN MATCHED THEN UPDATE SET Status=@Status,OverallScore=NULL,Summary=@Summary,Strengths=@Strengths,Concerns=@Concerns,Recommendation=@Recommendation,ModelName='H5_DEBUG_RULES',PromptVersion='h5-debug-v1',UpdateTime=SYSUTCDATETIME()
          WHEN NOT MATCHED THEN INSERT(SessionId,Status,OverallScore,Summary,Strengths,Concerns,Recommendation,ModelName,PromptVersion) VALUES(@SessionId,@Status,NULL,@Summary,@Strengths,@Concerns,@Recommendation,'H5_DEBUG_RULES','h5-debug-v1');
          UPDATE dbo.InterviewSession SET Status=@SessionStatus,SubmittedTime=COALESCE(SubmittedTime,SYSUTCDATETIME()),CompletedTime=CASE WHEN @SessionStatus='COMPLETED' THEN SYSUTCDATETIME() ELSE NULL END,UpdateTime=SYSUTCDATETIME() WHERE Id=@SessionId;
          UPDATE dbo.InterviewRtcTask SET Status='STOPPED',StoppedTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME() WHERE SessionId=@SessionId AND Status<>'STOPPED';
          """;
        await ExecuteAsync(connection,null,save,ct,("@SessionId",sessionId),("@Status",status),("@Summary",summary),("@Strengths",strengths),("@Concerns",concerns),("@Recommendation",recommendation),("@SessionStatus",status=="SUCCESS"?"COMPLETED":"REVIEW_REQUIRED"));
        return new(status,null,summary,strengths,concerns,recommendation);
    }

    private static async Task MarkTurnFailed(SqlConnection connection,long turnId,string error,CancellationToken ct)=>await ExecuteAsync(connection,null,"UPDATE dbo.InterviewTurn SET Status='ASR_FAILED',ErrorCode='ASR_FAILED',ErrorMessage=@Error,UpdateTime=SYSUTCDATETIME() WHERE Id=@Id",ct,("@Error",error[..Math.Min(error.Length,1000)]),("@Id",turnId));
    private static string NormalizePhone(string value){var digits=new string((value??string.Empty).Where(char.IsDigit).ToArray());return digits.StartsWith("86")&&digits.Length==13?digits[2..]:digits;}
    private static byte[] Combine(byte[] a,byte[] b){var result=new byte[a.Length+b.Length];Buffer.BlockCopy(a,0,result,0,a.Length);Buffer.BlockCopy(b,0,result,a.Length,b.Length);return result;}
    private static string Base64Url(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static BusinessException Bad(string code,string message,int status=400)=>new(code,message,status);
    private static async Task ExecuteAsync(SqlConnection connection,SqlTransaction? transaction,string sql,CancellationToken ct,params (string Name,object Value)[] values){await using var command=new SqlCommand(sql,connection,transaction);foreach(var x in values)command.Parameters.AddWithValue(x.Name,x.Value??DBNull.Value);await command.ExecuteNonQueryAsync(ct);}
    private static async Task<(long InvitationId,long AccessId)> GetAccessAsync(SqlConnection connection,SqlTransaction? transaction,string token,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(token))throw Bad("ACCESS_TOKEN_REQUIRED","请先验证手机号和邀请码。",401);
        var hash=SHA256.HashData(Encoding.UTF8.GetBytes(token));
        await using var command=new SqlCommand("SELECT a.Id,a.InvitationId FROM dbo.InterviewAccessSession a JOIN dbo.InterviewInvitation i ON i.Id=a.InvitationId AND i.IsDeleted=0 WHERE a.TokenHash=@Hash AND a.Status='BOUND' AND a.ExpiresTime>SYSUTCDATETIME()",connection,transaction);command.Parameters.Add("@Hash",SqlDbType.Binary,32).Value=hash;
        await using var reader=await command.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))throw Bad("ACCESS_TOKEN_INVALID","验证状态已过期，请重新验证。",401);return(reader.GetInt64(1),reader.GetInt64(0));
    }
}

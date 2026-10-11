using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RecruitAI.Application.Common;
using RecruitAI.Application.Knowledge;
using RecruitAI.Infrastructure.Database;

namespace RecruitAI.Infrastructure.Knowledge;

public sealed class SqlKnowledgeManagementService(SqlConnectionFactory factory) : IKnowledgeManagementService
{
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal) { "JOB_REQUIREMENT", "COMPANY_INFO", "INDUSTRY_INFO", "TECH_STANDARD", "TALENT_STANDARD", "SCORE_RULE", "OTHER" };
    private static readonly Regex CodePattern = new("^[A-Z0-9_-]{1,100}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<KnowledgeDocumentDto>> ListAsync(string? type, string? keyword, CancellationToken ct)
    {
        type = type?.Trim().ToUpperInvariant(); keyword = keyword?.Trim();
        const string sql = "SELECT Id,Code,Title,DocumentType,Content,Status,Version,RowVersion,UpdateTime FROM dbo.KnowledgeDocument WHERE IsDeleted=0 AND DocumentType<>'JOB_REQUIREMENT' AND (@Type IS NULL OR DocumentType=@Type) AND (@Keyword IS NULL OR Title LIKE N'%'+@Keyword+N'%' OR Content LIKE N'%'+@Keyword+N'%') ORDER BY DocumentType,Title,Id";
        await using var connection = await factory.OpenAsync(ct); await using var command = new SqlCommand(sql, connection);
        AddNullable(command, "@Type", SqlDbType.VarChar, 30, type); AddNullable(command, "@Keyword", SqlDbType.NVarChar, 200, keyword);
        await using var reader = await command.ExecuteReaderAsync(ct); var result = new List<KnowledgeDocumentDto>(); while (await reader.ReadAsync(ct)) result.Add(Map(reader)); return result;
    }

    public async Task<KnowledgeDocumentDto?> GetAsync(long id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct); await using var command = new SqlCommand("SELECT Id,Code,Title,DocumentType,Content,Status,Version,RowVersion,UpdateTime FROM dbo.KnowledgeDocument WHERE Id=@Id AND IsDeleted=0", connection); command.Parameters.AddWithValue("@Id", id);
        await using var reader = await command.ExecuteReaderAsync(ct); return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<long> CreateAsync(SaveKnowledgeDocumentRequest request, long userId, CancellationToken ct)
    {
        var item = Normalize(request); await using var connection = await factory.OpenAsync(ct);
        const string sql = "INSERT dbo.KnowledgeDocument(Code,Title,DocumentType,Content,Status,ContentHash,Version,CreatedBy,IsDeleted) VALUES(@Code,@Title,@Type,@Content,'DRAFT',@Hash,1,@UserId,0); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";
        await using var command = new SqlCommand(sql, connection); Add(command, item); command.Parameters.AddWithValue("@UserId", userId);
        try { return Convert.ToInt64(await command.ExecuteScalarAsync(ct)); } catch (SqlException ex) when (ex.Number is 2601 or 2627) { throw new BusinessException("KNOWLEDGE_CODE_EXISTS", "知识编码已存在。", 409); }
    }

    public async Task UpdateAsync(long id, SaveKnowledgeDocumentRequest request, CancellationToken ct)
    {
        var item = Normalize(request); var version = ParseVersion(request.RowVersion);
        const string sql = "UPDATE dbo.KnowledgeDocument SET Code=@Code,Title=@Title,DocumentType=@Type,Content=@Content,ContentHash=@Hash,Status='DRAFT',Version=Version+1,UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND IsDeleted=0 AND RowVersion=@Version";
        await using var connection = await factory.OpenAsync(ct); await using var command = new SqlCommand(sql, connection); Add(command, item); command.Parameters.AddWithValue("@Id", id); command.Parameters.Add("@Version", SqlDbType.Timestamp).Value = version;
        try { if (await command.ExecuteNonQueryAsync(ct) != 1) throw new BusinessException("CONCURRENCY_CONFLICT", "知识已被修改，请刷新。", 409); } catch (SqlException ex) when (ex.Number is 2601 or 2627) { throw new BusinessException("KNOWLEDGE_CODE_EXISTS", "知识编码已存在。", 409); }
    }

    public async Task PublishAsync(long id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct); await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            int version; await using (var update = new SqlCommand("UPDATE dbo.KnowledgeDocument SET Status='INDEXING',UpdateTime=SYSUTCDATETIME() OUTPUT INSERTED.Version WHERE Id=@Id AND IsDeleted=0 AND Status IN ('DRAFT','ACTIVE','FAILED','INDEXING')", connection, transaction)) { update.Parameters.AddWithValue("@Id", id); var value = await update.ExecuteScalarAsync(ct); if (value is null) throw new BusinessException("KNOWLEDGE_NOT_PUBLISHABLE", "知识不存在或不可发布。", 409); version = Convert.ToInt32(value); }
            const string taskSql = """
                IF EXISTS(SELECT 1 FROM dbo.AITask WHERE BusinessKey=@Key AND Status='SUCCESS')
                  UPDATE dbo.KnowledgeDocument SET Status='ACTIVE',UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND Version=@Version;
                ELSE IF EXISTS(SELECT 1 FROM dbo.AITask WHERE BusinessKey=@Key AND Status IN ('FAILED','CANCELLED'))
                  UPDATE dbo.AITask SET Status='WAITING',RetryCount=0,NextRunTime=SYSUTCDATETIME(),ErrorMessage=NULL,
                    ResultJson=NULL,WorkerId=NULL,LockedUntil=NULL,StartTime=NULL,FinishTime=NULL,UpdateTime=SYSUTCDATETIME()
                  WHERE BusinessKey=@Key;
                ELSE IF NOT EXISTS(SELECT 1 FROM dbo.AITask WHERE BusinessKey=@Key)
                  INSERT dbo.AITask(TaskType,KnowledgeDocumentId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey)
                  VALUES('KNOWLEDGE_EMBED',@Id,'WAITING',5,0,3,@Key);
                """;
            await using var task = new SqlCommand(taskSql, connection, transaction); task.Parameters.AddWithValue("@Id", id); task.Parameters.AddWithValue("@Version", version); task.Parameters.AddWithValue("@Key", $"knowledge-embed:{id}:{version}"); await task.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch { await transaction.RollbackAsync(ct); throw; }
    }

    public async Task ArchiveAsync(long id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct); await using var command = new SqlCommand("UPDATE dbo.KnowledgeDocument SET Status='ARCHIVED',UpdateTime=SYSUTCDATETIME() WHERE Id=@Id AND IsDeleted=0", connection); command.Parameters.AddWithValue("@Id", id); if (await command.ExecuteNonQueryAsync(ct) != 1) throw new BusinessException("KNOWLEDGE_NOT_FOUND", "知识不存在。", 404);
    }

    private static KnowledgeDocumentDto Map(SqlDataReader r) => new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt32(6), Convert.ToBase64String((byte[])r[7]), r.GetDateTime(8));
    private static (string Code,string Title,string Type,string Content,string Hash) Normalize(SaveKnowledgeDocumentRequest r) { var code=r.Code.Trim().ToUpperInvariant();var title=r.Title.Trim();var type=r.DocumentType.Trim().ToUpperInvariant();var content=r.Content.Trim();if(!CodePattern.IsMatch(code))throw new BusinessException("KNOWLEDGE_CODE_INVALID","知识编码只允许字母、数字、短横线和下划线。");if(title.Length is <1 or >300)throw new BusinessException("KNOWLEDGE_TITLE_INVALID","标题必须为1至300字。");if(!Types.Contains(type))throw new BusinessException("KNOWLEDGE_TYPE_INVALID","知识分类无效。");if(content.Length is <1 or >50000)throw new BusinessException("KNOWLEDGE_CONTENT_INVALID","内容必须为1至50000字。");var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.Replace("\r\n","\n").Replace('\r','\n'))));return(code,title,type,content,hash); }
    private static void Add(SqlCommand c,(string Code,string Title,string Type,string Content,string Hash) x){c.Parameters.Add("@Code",SqlDbType.NVarChar,100).Value=x.Code;c.Parameters.Add("@Title",SqlDbType.NVarChar,300).Value=x.Title;c.Parameters.AddWithValue("@Type",x.Type);c.Parameters.Add("@Content",SqlDbType.NVarChar,-1).Value=x.Content;c.Parameters.AddWithValue("@Hash",x.Hash);}
    private static byte[] ParseVersion(string? value){try{return Convert.FromBase64String(value??"");}catch{throw new BusinessException("INVALID_ROW_VERSION","并发版本格式无效。");}}
    private static void AddNullable(SqlCommand c,string name,SqlDbType type,int size,object? value)=>c.Parameters.Add(name,type,size).Value=value??DBNull.Value;
}

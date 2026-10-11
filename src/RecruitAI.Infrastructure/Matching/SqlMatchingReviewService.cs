using System.Data;
using Microsoft.Data.SqlClient;
using RecruitAI.Application.Common;
using RecruitAI.Application.Matching;
using RecruitAI.Infrastructure.Database;

namespace RecruitAI.Infrastructure.Matching;

public sealed class SqlMatchingReviewService(SqlConnectionFactory connectionFactory) : IMatchingReviewService
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.Ordinal)
        { "RECOMMENDED", "HOLD", "INTERVIEW", "REJECTED" };

    public async Task ReviewAsync(long candidateJobId, ReviewCandidateJobRequest request, long userId, CancellationToken cancellationToken)
    {
        if (request.ManualScore is < 0 or > 100) throw new BusinessException("INVALID_MANUAL_SCORE", "人工评分必须在0至100之间。 ");
        var status = request.Status.Trim().ToUpperInvariant();
        if (!AllowedStatuses.Contains(status)) throw new BusinessException("INVALID_REVIEW_STATUS", "复核状态无效。 ");
        var reason = request.Reason.Trim();
        if (reason.Length is < 1 or > 500) throw new BusinessException("INVALID_REVIEW_REASON", "复核原因必须为1至500字。 ");
        byte[] rowVersion;
        try { rowVersion = Convert.FromBase64String(request.RowVersion); }
        catch { throw new BusinessException("INVALID_ROW_VERSION", "并发版本格式无效。 "); }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            decimal? previousScore; string previousStatus;
            await using (var select = new SqlCommand("SELECT ManualScore,Status FROM dbo.CandidateJob WITH (UPDLOCK,HOLDLOCK) WHERE Id=@Id", connection, transaction))
            {
                select.Parameters.AddWithValue("@Id", candidateJobId);
                await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)) throw new BusinessException("CANDIDATE_JOB_NOT_FOUND", "候选人岗位匹配不存在。", 404);
                previousScore = reader.IsDBNull(0) ? null : reader.GetDecimal(0); previousStatus = reader.GetString(1);
            }
            await using (var update = new SqlCommand("""
                UPDATE dbo.CandidateJob SET ManualScore=@Score,Status=@Status,ReviewedBy=@UserId,
                  ReviewedTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME()
                WHERE Id=@Id AND RowVersion=@RowVersion
                """, connection, transaction))
            {
                update.Parameters.Add("@Score", SqlDbType.Decimal).Value = (object?)request.ManualScore ?? DBNull.Value;
                update.Parameters["@Score"].Precision = 5; update.Parameters["@Score"].Scale = 2;
                update.Parameters.AddWithValue("@Status", status); update.Parameters.AddWithValue("@UserId", userId); update.Parameters.AddWithValue("@Id", candidateJobId);
                update.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = rowVersion;
                if (await update.ExecuteNonQueryAsync(cancellationToken) != 1) throw new BusinessException("CONCURRENCY_CONFLICT", "评分记录已被修改，请刷新。", 409);
            }
            await using (var insert = new SqlCommand("""
                INSERT dbo.CandidateJobReview(CandidateJobId,PreviousManualScore,NewManualScore,PreviousStatus,NewStatus,Reason,ReviewedBy)
                VALUES(@Id,@PreviousScore,@NewScore,@PreviousStatus,@NewStatus,@Reason,@UserId)
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("@Id", candidateJobId); AddScore(insert, "@PreviousScore", previousScore); AddScore(insert, "@NewScore", request.ManualScore);
                insert.Parameters.AddWithValue("@PreviousStatus", previousStatus); insert.Parameters.AddWithValue("@NewStatus", status);
                insert.Parameters.Add("@Reason", SqlDbType.NVarChar, 500).Value = reason; insert.Parameters.AddWithValue("@UserId", userId);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    private static void AddScore(SqlCommand command, string name, decimal? value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal); parameter.Precision = 5; parameter.Scale = 2; parameter.Value = (object?)value ?? DBNull.Value;
    }
}

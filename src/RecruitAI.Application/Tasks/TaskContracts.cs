namespace RecruitAI.Application.Tasks;

public sealed record AiTaskDto(long Id, string TaskType, string Status, int RetryCount, int MaxRetryCount, string? ErrorMessage, DateTime CreateTime, DateTime UpdateTime);
public sealed record AiTaskPage(IReadOnlyList<AiTaskDto> Items, int TotalCount, int Page, int PageSize);
public sealed record AiTaskDetailDto(
    long Id, string TaskType, string Status, int RetryCount, int MaxRetryCount,
    string? ErrorMessage, string? WorkerId, DateTime? StartTime, DateTime? FinishTime,
    long? ResumeId, long? CandidateId, string? CandidateName, string? TalentId,
    string? ResumeFileName, int? ResumeTextLength, string? ResumeTextPreview,
    bool CanRetry, bool CanPurge, bool PurgeDeletesCandidate, int AffectedResumeCount,
    DateTime CreateTime, DateTime UpdateTime);

public interface IAiTaskService
{
    Task<AiTaskPage> ListAsync(int page, int pageSize, string? type, string? status, CancellationToken ct);
    Task<AiTaskDetailDto?> GetAsync(long id, CancellationToken ct);
    Task RetryAsync(long id, CancellationToken ct);
    Task CancelAsync(long id, CancellationToken ct);
    Task<int> CancelAllWaitingAsync(CancellationToken ct);
    Task<int> ClearAllFailedAsync(long userId, CancellationToken ct);
    Task PurgeAsync(long id, long userId, CancellationToken ct);
}

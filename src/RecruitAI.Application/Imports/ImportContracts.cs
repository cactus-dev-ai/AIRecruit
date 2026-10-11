namespace RecruitAI.Application.Imports;

public sealed record ResumeScanResult(
    string Result, long? BatchId, int SelectedCount, int ReviewReadyCount,
    int FailedValidationCount, int HrUploadCount, int ThirdPartyCount,
    bool HasMore, string Message);

public sealed record ResumeImportBatchSummary(
    long Id, Guid BatchKey, string Status, string? Remark, int SourceFileCount,
    int SuccessCount, int FailedCount, string CreatedBy, DateTime CreateTime,
    string RowVersion);

public sealed record ResumeImportBatchPage(
    IReadOnlyList<ResumeImportBatchSummary> Items, int TotalCount, int Page, int PageSize);

public sealed record ResumeImportFileDto(
    long Id, string SourceType, string OriginalFileName, string FileExtension,
    long FileSize, string Status, string? ErrorCode, string? ErrorMessage,
    int? DetectedResumeCount, DateTime CreateTime);

public sealed record ResumeImportBatchDetail(
    ResumeImportBatchSummary Batch, IReadOnlyList<ResumeImportFileDto> Files,
    IReadOnlyList<long> JobIds);

public sealed record ConfirmResumeImportRequest(
    IReadOnlyList<long> JobIds, string? Remark, string RowVersion);

public sealed record ConfirmResumeImportResult(long BatchId, int TaskCount);
public sealed record ManualResumeUploadFile(string FileName, long Length, Stream Content);
public sealed record ManualResumeUploadResult(long BatchId, int FileCount, string Status, string Message);
public sealed record PurgeFailedFileRequest(string Reason);
public sealed record ReturnFailedFileRequest(string Reason);
public sealed record ResumeImportFailurePreview(long Id, string OriginalFileName, string FileExtension,
    string? ErrorCode, string? ErrorMessage, string? PreviewText, bool IsPreviewAvailable);

public interface IResumeImportService
{
    Task<ResumeScanResult> ScanAsync(long userId, CancellationToken cancellationToken);
    Task<ManualResumeUploadResult> UploadManualAsync(IReadOnlyList<ManualResumeUploadFile> files, long jobId, long userId, CancellationToken cancellationToken);
    Task<ResumeImportBatchPage> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<ResumeImportBatchDetail?> GetAsync(long batchId, CancellationToken cancellationToken);
    Task RejectAsync(long batchId, long fileId, long userId, bool isAdmin, CancellationToken cancellationToken);
    Task<ConfirmResumeImportResult> ConfirmAsync(long batchId, ConfirmResumeImportRequest request, long userId, bool isAdmin, CancellationToken cancellationToken);
    Task CancelReviewAsync(long batchId, long userId, bool isAdmin, CancellationToken cancellationToken);
    Task<IReadOnlyList<ResumeImportFileDto>> ListFailedAsync(CancellationToken cancellationToken);
    Task<ResumeImportFailurePreview> GetFailedPreviewAsync(long fileId, CancellationToken cancellationToken);
    Task ReturnFailedAsync(long fileId, string reason, long userId, CancellationToken cancellationToken);
    Task PurgeFailedAsync(long fileId, string reason, long userId, CancellationToken cancellationToken);
}

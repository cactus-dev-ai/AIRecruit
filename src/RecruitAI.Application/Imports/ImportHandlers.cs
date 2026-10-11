namespace RecruitAI.Application.Imports;

public sealed class RejectImportFileHandler(IResumeImportService service)
{
    public Task HandleAsync(long batchId, long fileId, long userId, bool isAdmin, CancellationToken ct) => service.RejectAsync(batchId, fileId, userId, isAdmin, ct);
}

public sealed class ConfirmImportBatchHandler(IResumeImportService service)
{
    public Task<ConfirmResumeImportResult> HandleAsync(long batchId, ConfirmResumeImportRequest request, long userId, bool isAdmin, CancellationToken ct) => service.ConfirmAsync(batchId, request, userId, isAdmin, ct);
}

public sealed class CancelImportReviewHandler(IResumeImportService service)
{
    public Task HandleAsync(long batchId, long userId, bool isAdmin, CancellationToken ct) => service.CancelReviewAsync(batchId, userId, isAdmin, ct);
}

public sealed class PurgeFailedImportFileHandler(IResumeImportService service)
{
    public Task HandleAsync(long fileId, string reason, long userId, CancellationToken ct) => service.PurgeFailedAsync(fileId, reason, userId, ct);
}

public sealed class ReturnFailedImportFileHandler(IResumeImportService service)
{
    public Task HandleAsync(long fileId, string reason, long userId, CancellationToken ct) => service.ReturnFailedAsync(fileId, reason, userId, ct);
}

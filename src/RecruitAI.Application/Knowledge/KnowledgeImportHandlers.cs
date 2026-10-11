namespace RecruitAI.Application.Knowledge;

public sealed class UploadKnowledgeExcelHandler(IKnowledgeImportService service)
{
    public Task<long> HandleAsync(Stream stream, string fileName, long length, long userId, CancellationToken ct) => service.ParseExcelAsync(stream, fileName, length, userId, ct);
}

public sealed class UpdateKnowledgeImportRowHandler(IKnowledgeImportService service)
{
    public Task HandleAsync(long batchId, long rowId, UpdateKnowledgeImportRowRequest request, long userId, CancellationToken ct) => service.UpdateRowAsync(batchId, rowId, request, userId, ct);
}

public sealed class ConfirmKnowledgeImportHandler(IKnowledgeImportService service)
{
    public Task<int> HandleAsync(long batchId, long userId, CancellationToken ct) => service.ConfirmAsync(batchId, userId, ct);
}

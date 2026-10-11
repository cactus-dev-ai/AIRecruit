namespace RecruitAI.Application.Knowledge;

public sealed record KnowledgeImportBatchDto(
    long Id, string OriginalFileName, string Status, int TotalCount,
    int ReadyCount, int DuplicateCount, int ConflictCount, int InvalidCount,
    string RowVersion, IReadOnlyList<KnowledgeImportRowDto> Rows);

public sealed record KnowledgeImportRowDto(
    long Id, int RowNumber, string Code, string Title, string DocumentType,
    string Content, string Status, long? ExistingDocumentId,
    string? ErrorMessage, string RowVersion);

public sealed record UpdateKnowledgeImportRowRequest(
    string Code, string Title, string DocumentType, string Content, string RowVersion);

public interface IKnowledgeImportService
{
    Task<long> ParseExcelAsync(Stream stream, string fileName, long fileLength, long userId, CancellationToken cancellationToken);
    Task<KnowledgeImportBatchDto?> GetAsync(long batchId, CancellationToken cancellationToken);
    Task UpdateRowAsync(long batchId, long rowId, UpdateKnowledgeImportRowRequest request, long userId, CancellationToken cancellationToken);
    Task<int> ConfirmAsync(long batchId, long userId, CancellationToken cancellationToken);
}

public sealed record KnowledgeDocumentDto(long Id, string Code, string Title, string DocumentType, string Content, string Status, int Version, string RowVersion, DateTime UpdateTime);
public sealed record SaveKnowledgeDocumentRequest(string Code, string Title, string DocumentType, string Content, string? RowVersion);
public sealed record KnowledgeAiDraftRequest(string Title, string DocumentType, string? Content, string? Instruction);
public sealed record KnowledgeAiDraftResponse(string Content);

public interface IKnowledgeManagementService
{
    Task<IReadOnlyList<KnowledgeDocumentDto>> ListAsync(string? type, string? keyword, CancellationToken ct);
    Task<KnowledgeDocumentDto?> GetAsync(long id, CancellationToken ct);
    Task<long> CreateAsync(SaveKnowledgeDocumentRequest request, long userId, CancellationToken ct);
    Task UpdateAsync(long id, SaveKnowledgeDocumentRequest request, CancellationToken ct);
    Task PublishAsync(long id, CancellationToken ct);
    Task ArchiveAsync(long id, CancellationToken ct);
}

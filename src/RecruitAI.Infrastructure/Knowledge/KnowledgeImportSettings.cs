namespace RecruitAI.Infrastructure.Knowledge;

public sealed record KnowledgeImportSettings(long MaxFileBytes, int MaxRows, string RequiredWorksheetName);

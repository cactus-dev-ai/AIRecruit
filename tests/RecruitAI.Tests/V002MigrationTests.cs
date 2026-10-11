namespace RecruitAI.Tests;

public sealed class V002MigrationTests
{
    [Fact]
    public void Migration_UsesSqlServer2008R2CompatibleSurface()
    {
        var sql = File.ReadAllText(FindMigration());
        foreach (var forbidden in new[] { "ISJSON(", "OFFSET ", "FETCH NEXT", "THROW;", "CREATE OR ALTER", "STRING_SPLIT(" })
            Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_ContainsAllApprovedTablesAndTaskExtension()
    {
        var sql = File.ReadAllText(FindMigration());
        foreach (var table in new[] { "ResumeImportBatch", "ResumeImportFile", "ResumeImportBatchJob", "ResumeImportResult", "CandidateJobReview", "KnowledgeImportBatch", "KnowledgeImportRow" })
            Assert.Contains($"dbo.{table}", sql, StringComparison.Ordinal);
        Assert.Contains("ResumeImportFileId", sql, StringComparison.Ordinal);
        Assert.Contains("'RESUME_IMPORT'", sql, StringComparison.Ordinal);
        Assert.Contains("OriginalFileSize", sql, StringComparison.Ordinal);
        Assert.Contains("IsOptimized", sql, StringComparison.Ordinal);
    }

    private static string FindMigration()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "database", "V002__web_import_review_knowledge.sql");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException("V002 migration was not found.");
    }
}

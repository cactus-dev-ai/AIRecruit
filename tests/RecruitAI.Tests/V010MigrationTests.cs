namespace RecruitAI.Tests;

public sealed class V010MigrationTests
{
    [Fact]
    public void Migration_DefinesOnePrimaryJobProfileAndKeepsSqlServer2008Compatibility()
    {
        var sql = File.ReadAllText(FindMigration());
        Assert.Contains("dbo.JobKnowledgeDocument", sql, StringComparison.Ordinal);
        Assert.Contains("UX_JobKnowledgeDocument_Primary", sql, StringComparison.Ordinal);
        Assert.Contains("PRIMARY_REQUIREMENT", sql, StringComparison.Ordinal);
        Assert.Contains("JOB-PROFILE-", sql, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "THROW;", "CREATE OR ALTER", "STRING_AGG(", "OPENJSON(", "ISJSON(" })
            Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindMigration()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "database", "V010__job_profile_management.sql");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException("V010 migration was not found.");
    }
}

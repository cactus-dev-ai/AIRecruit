namespace RecruitAI.Tests;

public sealed class V003MigrationTests
{
    [Fact]
    public void Migration_AllowsJsonAndRemainsSqlServer2008Compatible()
    {
        var sql = File.ReadAllText(FindMigration());
        Assert.Contains("'JSON'", sql, StringComparison.Ordinal);
        Assert.Contains("CK_ResumeImportFile_Extension", sql, StringComparison.Ordinal);
        Assert.Contains("CK_Resume_Extension", sql, StringComparison.Ordinal);
        Assert.Contains("TalentId NVARCHAR(100)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ResumeSource NVARCHAR(100)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IX_Resume_Source_TalentId", sql, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "ISJSON(", "THROW;", "CREATE OR ALTER", "DROP CONSTRAINT IF EXISTS" })
            Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindMigration()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "database", "V003__resume_json_import.sql");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException("V003 migration was not found.");
    }
}

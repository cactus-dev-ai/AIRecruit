namespace RecruitAI.Tests;

public sealed class V017MigrationTests
{
    private static string ReadMigration()
    {
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","database","V017__manual_resume_import.sql"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void Migration_EnablesManualPdfAndDocxAgentTasks()
    {
        var sql=ReadMigration();
        Assert.Contains("'JSON','PDF','DOCX'",sql,StringComparison.Ordinal);
        Assert.Contains("'THIRD_PARTY_IMPORT','HR_UPLOAD'",sql,StringComparison.Ordinal);
        Assert.Contains("'MANUAL_RESUME_IMPORT'",sql,StringComparison.Ordinal);
        Assert.Contains("ResumeImportFileId IS NOT NULL",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE OR ALTER",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ISJSON",sql,StringComparison.OrdinalIgnoreCase);
    }
}

namespace RecruitAI.Tests;

public sealed class V016MigrationTests
{
    private static string ReadMigration()
    {
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","database","V016__interview_delete_and_report_submission.sql"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void Migration_AddsSafeDeleteAndStructuredReportFields()
    {
        var sql=ReadMigration();
        Assert.Contains("IsDeleted BIT NOT NULL",sql,StringComparison.Ordinal);
        Assert.Contains("DeletedBy BIGINT NULL",sql,StringComparison.Ordinal);
        Assert.Contains("ResumeKeyInfo NVARCHAR(MAX)",sql,StringComparison.Ordinal);
        Assert.Contains("VoiceInterviewInfo NVARCHAR(MAX)",sql,StringComparison.Ordinal);
        Assert.Contains("WHERE i.IsDeleted=0",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE OR ALTER",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ISJSON",sql,StringComparison.OrdinalIgnoreCase);
    }
}

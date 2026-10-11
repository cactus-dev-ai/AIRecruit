namespace RecruitAI.Tests;

public sealed class V018MigrationTests
{
    private static string ReadMigration()
    {
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","database","V018__job_candidate_cleanup_rules.sql"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void Migration_AddsValidatedAgeAndMonthlySalaryLimitsIdempotently()
    {
        var sql=ReadMigration();
        Assert.Contains("COL_LENGTH('dbo.Job', 'MaxCandidateAge') IS NULL",sql,StringComparison.Ordinal);
        Assert.Contains("COL_LENGTH('dbo.Job', 'MaxExpectedMonthlySalary') IS NULL",sql,StringComparison.Ordinal);
        Assert.Contains("CK_Job_MaxCandidateAge",sql,StringComparison.Ordinal);
        Assert.Contains("CK_Job_MaxExpectedMonthlySalary",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE OR ALTER",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ISJSON",sql,StringComparison.OrdinalIgnoreCase);
    }
}

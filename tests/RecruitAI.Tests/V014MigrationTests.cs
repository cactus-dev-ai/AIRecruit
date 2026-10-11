namespace RecruitAI.Tests;

public sealed class V014MigrationTests
{
    private static string ReadMigration()
    {
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","database","V014__scoring_run_history.sql"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void Migration_CreatesImmutableScoringRunsAndTaskLink()
    {
        var sql=ReadMigration();
        Assert.Contains("CREATE TABLE dbo.ScoringRun(",sql,StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE dbo.ScoringRunResult(",sql,StringComparison.Ordinal);
        Assert.Contains("UQ_ScoringRun_Job_RunNo",sql,StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE dbo.AITask ADD ScoringRunId BIGINT NULL",sql,StringComparison.Ordinal);
        Assert.Contains("ResumeImportTime",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("ISJSON",sql,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_BackfillsExistingRankingAsFirstRun()
    {
        var sql=ReadMigration();
        Assert.Contains("Preserve the latest pre-V014 ranking as run 1",sql,StringComparison.Ordinal);
        Assert.Contains("INSERT dbo.ScoringRunResult",sql,StringComparison.Ordinal);
        Assert.Contains("UPDATE t SET ScoringRunId=sr.Id",sql,StringComparison.Ordinal);
    }
}

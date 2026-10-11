namespace RecruitAI.Tests;

public sealed class V019MigrationTests
{
    [Fact]
    public void Migration_CreatesRestorableCleanupHistoryIdempotently()
    {
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","database","V019__job_candidate_cleanup_history.sql"));
        var sql=File.ReadAllText(path);
        Assert.Contains("OBJECT_ID('dbo.JobCandidateCleanupHistory', 'U') IS NULL",sql,StringComparison.Ordinal);
        Assert.Contains("PreviousStatus",sql,StringComparison.Ordinal);
        Assert.Contains("PreviousIsFavorite",sql,StringComparison.Ordinal);
        Assert.Contains("IsRestored",sql,StringComparison.Ordinal);
        Assert.Contains("IX_JobCandidateCleanupHistory_Restore",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE OR ALTER",sql,StringComparison.OrdinalIgnoreCase);
    }
}

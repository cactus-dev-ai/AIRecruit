namespace RecruitAI.Tests;

public sealed class JobMergeSqlContractTests
{
    [Fact]
    public void MergeJob_PreservesDuplicateCandidateRelationsAndMovesOnlyUniqueCandidates()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RecruitAI.Infrastructure", "Recruitment", "SqlRecruitmentService.cs"));
        var source = File.ReadAllText(path);
        var mergeStart = source.IndexOf("const string mergeSql", StringComparison.Ordinal);
        var mergeEnd = source.IndexOf("await using (var merge", mergeStart, StringComparison.Ordinal);

        Assert.True(mergeStart >= 0 && mergeEnd > mergeStart);
        var mergeSql = source[mergeStart..mergeEnd];

        Assert.DoesNotContain("DELETE s FROM dbo.CandidateJob", mergeSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE s.JobId=@SourceId AND NOT EXISTS", mergeSql, StringComparison.Ordinal);
        Assert.Contains("t.CandidateId=s.CandidateId AND t.JobId=@TargetId", mergeSql, StringComparison.Ordinal);
        Assert.Contains("UPDATE h SET JobId=@TargetId", mergeSql, StringComparison.Ordinal);
        Assert.Contains("FROM dbo.JobCandidateCleanupHistory h JOIN dbo.CandidateJob cj", mergeSql, StringComparison.Ordinal);
    }
}

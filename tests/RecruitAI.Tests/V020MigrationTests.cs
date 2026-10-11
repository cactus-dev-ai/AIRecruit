namespace RecruitAI.Tests;

public sealed class V020MigrationTests
{
    [Fact]
    public void Migration_RepairsManual51JobIdentityAndAnalysisState()
    {
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","database","V020__manual_resume_identity_and_analysis_repair.sql"));
        var sql=File.ReadAllText(path);
        Assert.Contains("TalentId=x.TalentId,ResumeSource='51JOB'",sql,StringComparison.Ordinal);
        Assert.Contains("UPDATE c SET Source='51JOB'",sql,StringComparison.Ordinal);
        Assert.Contains("UPDATE r SET IsAiAnalyzed=1",sql,StringComparison.Ordinal);
        Assert.Contains("JOIN dbo.CandidateProfile p ON p.ResumeId=r.Id",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("TRY_CONVERT",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE OR ALTER",sql,StringComparison.OrdinalIgnoreCase);
    }
}

namespace RecruitAI.Tests;

public sealed class RankingTalentIdFilterTests
{
    private static string Source(string relativePath)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..",".."));
        return File.ReadAllText(Path.Combine(root,relativePath));
    }

    [Fact]
    public void RankingApi_Exposes51JobTalentIdFilter()
    {
        var controller=Source(Path.Combine("src","RecruitAI.Web","Controllers","JobsController.cs"));
        var view=Source(Path.Combine("src","RecruitAI.Web","Views","Home","Scores.cshtml"));
        Assert.Contains("string? talentId=null",controller,StringComparison.Ordinal);
        Assert.Contains("id=\"ranking-talent-id\"",view,StringComparison.Ordinal);
        Assert.Contains("query.set('talentId',talentId)",view,StringComparison.Ordinal);
    }

    [Fact]
    public void RankingSql_AppliesTalentIdToCountsAndRows()
    {
        var service=Source(Path.Combine("src","RecruitAI.Infrastructure","Recruitment","SqlRecruitmentService.cs"));
        Assert.True(service.Split("@TalentId IS NULL OR",StringSplitOptions.None).Length>=4);
        Assert.Contains("tr.Id=r.ResumeId AND tr.TalentId=@TalentId",service,StringComparison.Ordinal);
        Assert.Contains("tr.Id=rr.ResumeId AND tr.TalentId=@TalentId",service,StringComparison.Ordinal);
        Assert.Contains("SqlDbType.NVarChar,100",service,StringComparison.Ordinal);
    }
}

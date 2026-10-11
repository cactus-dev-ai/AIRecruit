namespace Job51.Tests;

public sealed class TalentSearchFilterTests
{
    private static readonly Job51.WinForms.TalentJobApiData Job = new()
    {
        JobId = "123",
        JobName = "测试岗位",
        FunctionTypeCode = "456",
        AiKeyword = "机械设计"
    };

    [Fact]
    public void SearchScript_ExcludesViewedCandidatesByDefault()
    {
        var script = Job51.WinForms.BrowserScripts.StartTalentSearchViaPageApi(Job);
        Assert.Contains("\"chk_unread_resume\":\"1\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchScript_CanIncludeViewedCandidatesWhenConfigured()
    {
        var script = Job51.WinForms.BrowserScripts.StartTalentSearchViaPageApi(
            Job, excludeViewedCandidates: false);
        Assert.Contains("\"chk_unread_resume\":\"\"", script, StringComparison.Ordinal);
    }
}

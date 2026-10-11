namespace RecruitAI.Tests;

public sealed class WebResponseContractTests
{
    private static string Source(string relativePath)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..",".."));
        return File.ReadAllText(Path.Combine(root,relativePath));
    }

    [Fact]
    public void ApiClient_AllowsSuccessfulEmptyResponseBodies()
    {
        var layout=Source(Path.Combine("src","RecruitAI.Web","Views","Shared","_Layout.cshtml"));
        Assert.Contains("if (!text.trim()) return null",layout,StringComparison.Ordinal);
    }

    [Fact]
    public void AuthenticationCookies_UseStableApplicationAndCookieNames()
    {
        var program=Source(Path.Combine("src","RecruitAI.Web","Program.cs"));
        Assert.Contains("SetApplicationName(\"RecruitAI.Web\")",program,StringComparison.Ordinal);
        Assert.Contains("options.Cookie.Name = \"RecruitAI.Antiforgery\"",program,StringComparison.Ordinal);
        Assert.Contains("options.Cookie.Name = \"RecruitAI.Auth\"",program,StringComparison.Ordinal);
    }
}

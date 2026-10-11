namespace RecruitAI.Tests;

public sealed class JobPublishContractTests
{
    private static string Source(string relativePath)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..",".."));
        return File.ReadAllText(Path.Combine(root,relativePath));
    }

    [Fact]
    public void PublishingProfile_AlsoOpensJob()
    {
        var controller=Source(Path.Combine("src","RecruitAI.Web","Controllers","JobsController.cs"));
        Assert.Contains("await knowledge.PublishAsync(profile.KnowledgeDocumentId,ct)",controller,StringComparison.Ordinal);
        Assert.Contains("if(job.Status!=\"OPEN\")await service.ChangeJobStatusAsync(id,\"OPEN\",ct)",controller,StringComparison.Ordinal);
    }

    [Fact]
    public void PublishingProfile_IsIdempotentWhileIndexing()
    {
        var service=Source(Path.Combine("src","RecruitAI.Infrastructure","Knowledge","SqlKnowledgeManagementService.cs"));
        Assert.Contains("'DRAFT','ACTIVE','FAILED','INDEXING'",service,StringComparison.Ordinal);
    }
}

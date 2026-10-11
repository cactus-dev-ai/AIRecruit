using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using RecruitAI.Web.Controllers;

namespace RecruitAI.Tests;

public sealed class ThirdPartyResumesControllerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RecruitAI.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Upload_SavesValidJsonAndDeduplicatesByContent()
    {
        var first = await UploadAsync("test-key", "test-key", "{\"talent_id\":\"1001\",\"source_job\":{\"provider\":\"51JOB\",\"external_job_id\":\"job-1\",\"external_job_name\":\"测试岗位\"}}");
        var second = await UploadAsync("test-key", "test-key", "{\"talent_id\":\"1001\",\"source_job\":{\"provider\":\"51JOB\",\"external_job_id\":\"job-1\",\"external_job_name\":\"测试岗位\"}}");

        Assert.IsType<CreatedResult>(first);
        Assert.IsType<OkObjectResult>(second);
        var uploadedFile = Assert.Single(Directory.GetFiles(_directory, "*.json"));
        Assert.True(
            File.GetLastWriteTimeUtc(uploadedFile) <= DateTime.UtcNow.AddSeconds(-30),
            "Completed HTTP uploads must be immediately eligible for the stable-file scanner.");
    }

    [Fact]
    public async Task Upload_RejectsInvalidApiKey()
    {
        var result = await UploadAsync("wrong-key", "test-key", "{\"talent_id\":\"1001\",\"source_job\":{\"provider\":\"51JOB\",\"external_job_id\":\"job-1\",\"external_job_name\":\"测试岗位\"}}");

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task Upload_RejectsInvalidJson()
    {
        var result = await UploadAsync("test-key", "test-key", "not-json");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private async Task<IActionResult> UploadAsync(string suppliedKey, string configuredKey, string json)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ThirdPartyResumeImport:Directory"] = _directory,
            ["ThirdPartyResumeImport:ApiKey"] = configuredKey
        }).Build();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-API-Key"] = suppliedKey;
        context.Request.Headers["X-Resume-File-Name"] = "candidate_test.json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var controller = new ThirdPartyResumesController(configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
        return await controller.Upload(CancellationToken.None);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecruitAI.Application.Interviews;

namespace RecruitAI.Web.Controllers;

[AllowAnonymous]
public sealed class PublicInterviewController(IPublicInterviewService service) : Controller
{
    [HttpGet("/interview")]
    public IActionResult Index() => View();

    [IgnoreAntiforgeryToken]
    [HttpPost("/api/public/interview/verify")]
    public Task<VerifiedInterviewDto> Verify([FromBody] VerifyInterviewCodeRequest request, CancellationToken ct) =>
        service.VerifyAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), ct);

    [IgnoreAntiforgeryToken]
    [HttpPost("/api/public/interview/start")]
    public Task<PublicInterviewRoomDto> Start([FromBody] StartPublicInterviewRequest request, CancellationToken ct) =>
        service.StartAsync(Token(), request, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

    [IgnoreAntiforgeryToken]
    [HttpPost("/api/public/interview/recording/start")]
    public async Task<IActionResult> BeginRecording(CancellationToken ct)
    {
        await service.BeginRecordingAsync(Token(), ct);
        return NoContent();
    }

    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(3_200_000)]
    [HttpPost("/api/public/interview/turns/{turnId:long}/answer")]
    public async Task<PublicTurnResultDto> Answer(long turnId, IFormFile audio, [FromForm] int durationMs, CancellationToken ct)
    {
        await using var stream = audio.OpenReadStream();
        return await service.SubmitAnswerAsync(Token(), turnId, stream, audio.Length, audio.ContentType, durationMs, ct);
    }

    [IgnoreAntiforgeryToken]
    [HttpPost("/api/public/interview/submit")]
    public Task<PublicInterviewReportDto> Submit(CancellationToken ct) => service.SubmitAsync(Token(), ct);

    private string Token()
    {
        var token = Request.Headers["X-Interview-Token"].ToString();
        if (string.IsNullOrWhiteSpace(token) && Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = Request.Headers.Authorization.ToString()[7..].Trim();
        return token;
    }
}

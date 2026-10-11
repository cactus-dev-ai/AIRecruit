using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecruitAI.Infrastructure.Interviews;

namespace RecruitAI.Web.Controllers;

[ApiController]
[Authorize(Policy = "ADMIN")]
[Route("api/admin/notification-diagnostics")]
public sealed class NotificationDiagnosticsController(
    InterviewInvitationNotifier notifier,
    IHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var process=Process.GetCurrentProcess();
        return Ok(new
        {
            provider="ALIYUN_SMS",
            sms=notifier.GetSmsDiagnostics(),
            processId=process.Id,
            processStartTimeUtc=process.StartTime.ToUniversalTime(),
            environment=environment.EnvironmentName,
            machine=Environment.MachineName
        });
    }
}

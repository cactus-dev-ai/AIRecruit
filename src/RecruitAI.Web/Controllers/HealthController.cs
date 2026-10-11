using Microsoft.AspNetCore.Mvc;
using RecruitAI.Infrastructure.Database;

namespace RecruitAI.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class HealthController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public HealthController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "ok",
            service = "RecruitAI.Web",
            utcTime = DateTime.UtcNow
        });
    }

    [HttpGet("database")]
    public async Task<IActionResult> GetDatabase(CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString("RecruitAI");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return StatusCode(503, new { status = "error", message = "Missing RecruitAI connection string." });
        }

        try
        {
            var result = await SqlServerProbe.CheckAsync(connectionString, cancellationToken);
            return Ok(new
            {
                status = "ok",
                database = result.Database,
                serverVersion = result.ProductVersion,
                tableCount = result.TableCount
            });
        }
        catch (Exception exception)
        {
            return StatusCode(503, new { status = "error", message = exception.Message });
        }
    }
}

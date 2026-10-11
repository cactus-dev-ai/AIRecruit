using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecruitAI.Application.Tasks;
using RecruitAI.Infrastructure.Tasks;
using RecruitAI.Web.Infrastructure;

namespace RecruitAI.Web.Controllers;

[ApiController]
[Authorize(Policy="Recruitment")]
[Route("api/ai-tasks")]
public sealed class AiTasksController(IAiTaskService service, AgentWorkerControlService workerControl):ControllerBase
{
    [HttpGet] public Task<AiTaskPage> List(int page=1,int pageSize=10,string? type=null,string? status="ACTIVE",CancellationToken ct=default)=>service.ListAsync(page,pageSize,type,status,ct);
    [HttpGet("{id:long}")] public async Task<ActionResult<AiTaskDetailDto>> Get(long id,CancellationToken ct){var item=await service.GetAsync(id,ct);return item is null?NotFound():item;}
    [HttpPost("{id:long}/retry")] public async Task<IActionResult> Retry(long id,CancellationToken ct){await service.RetryAsync(id,ct);return NoContent();}
    [HttpPost("{id:long}/cancel")] public async Task<IActionResult> Cancel(long id,CancellationToken ct){await service.CancelAsync(id,ct);return NoContent();}
    [HttpPost("cancel-all")] public async Task<ActionResult<object>> CancelAll(CancellationToken ct)
    {
        var worker=await workerControl.GetStatusAsync(ct);
        if(!worker.ServiceOnline||worker.Running)
            throw new RecruitAI.Application.Common.BusinessException("AI_WORKER_NOT_PAUSED","请先暂停AI任务，并等待当前处理任务全部结束后再执行一键取消。",409);
        var cancelled=await service.CancelAllWaitingAsync(ct);
        return Ok(new { cancelled });
    }
    [HttpPost("clear-failed")] public async Task<ActionResult<object>> ClearFailed(CancellationToken ct)
    {
        var cleared=await service.ClearAllFailedAsync(User.GetUserId(),ct);
        return Ok(new { cleared });
    }
    [HttpDelete("{id:long}")]
    [HttpPost("{id:long}/purge")] public async Task<IActionResult> Purge(long id,CancellationToken ct){await service.PurgeAsync(id,User.GetUserId(),ct);return NoContent();}
    [HttpGet("worker")] public Task<AgentWorkerStatus> Worker(CancellationToken ct)=>workerControl.GetStatusAsync(ct);
    [HttpPost("worker/start")] public async Task<AgentWorkerStatus> StartWorker(CancellationToken ct)=>await workerControl.StartAsync(ct);
    [HttpPost("worker/pause")] public async Task<AgentWorkerStatus> PauseWorker(CancellationToken ct)=>await workerControl.PauseAsync(ct);
}

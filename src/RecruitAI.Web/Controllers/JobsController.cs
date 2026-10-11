using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecruitAI.Application.Recruitment;
using RecruitAI.Application.Knowledge;
using RecruitAI.Web.Infrastructure;

namespace RecruitAI.Web.Controllers;

[ApiController]
[Authorize(Policy="Recruitment")]
[Route("api/jobs")]
public sealed class JobsController(IRecruitmentService service,IKnowledgeManagementService knowledge,KnowledgeAiDraftService ai):ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<JobDto>> List(int page=1,int pageSize=20,string? status=null,string? keyword=null,CancellationToken ct=default)=>service.ListJobsAsync(page,pageSize,status,keyword,ct);
    [HttpGet("{id:long}")] public async Task<ActionResult<JobDto>> Get(long id,CancellationToken ct){var item=await service.GetJobAsync(id,ct);return item is null?NotFound():item;}
    [HttpPost] public async Task<IActionResult> Create(SaveJobRequest request,CancellationToken ct){var id=await service.CreateJobAsync(request,User.GetUserId(),ct);return Created($"/api/jobs/{id}",new{id});}
    [HttpPut("{id:long}")]
    [HttpPost("{id:long}/update")] public async Task<IActionResult> Update(long id,SaveJobRequest request,CancellationToken ct){await service.UpdateJobAsync(id,request,ct);return NoContent();}
    [HttpPost("{id:long}/open")] public async Task<IActionResult> Open(long id,CancellationToken ct){await service.ChangeJobStatusAsync(id,"OPEN",ct);return NoContent();}
    [HttpPost("{id:long}/close")] public async Task<IActionResult> Close(long id,CancellationToken ct){await service.ChangeJobStatusAsync(id,"CLOSE",ct);return NoContent();}
    [HttpDelete("{id:long}")]
    [HttpPost("{id:long}/archive")] public async Task<IActionResult> Delete(long id,CancellationToken ct){await service.DeleteJobAsync(id,ct);return NoContent();}
    [HttpPut("{id:long}/cleanup-rules")]
    [HttpPost("{id:long}/cleanup-rules/update")] public async Task<IActionResult> CleanupRules(long id,UpdateJobCleanupRulesRequest request,CancellationToken ct){await service.UpdateJobCleanupRulesAsync(id,request,ct);return NoContent();}
    [HttpPost("{id:long}/cleanup/{criterion}")] public Task<JobCleanupResult> Cleanup(long id,string criterion,CancellationToken ct)=>service.CleanupJobCandidatesAsync(id,criterion,User.GetUserId(),ct);
    [HttpPost("{id:long}/cleanup/restore")] public Task<JobCleanupRestoreResult> RestoreCleanup(long id,CancellationToken ct)=>service.RestoreJobCandidatesAsync(id,User.GetUserId(),ct);
    [HttpPut("{id:long}/score-config")]
    [HttpPost("{id:long}/score-config/update")] public async Task<IActionResult> Score(long id,UpdateScoreConfigRequest request,CancellationToken ct){await service.UpdateScoreConfigAsync(id,request,User.GetUserId(),ct);return NoContent();}
    [HttpPost("{id:long}/score")] public async Task<IActionResult> QueueScore(long id,CancellationToken ct){var result=await service.QueueJobScoringAsync(id,User.GetUserId(),ct);return Accepted(result);}
    [HttpGet("{id:long}/score-runs")] public Task<IReadOnlyList<ScoreRunDto>> ScoreRuns(long id,CancellationToken ct)=>service.ListScoreRunsAsync(id,ct);
    [HttpGet("{id:long}/ranking")] public Task<JobRankingPage> Ranking(long id,long? runId=null,int page=1,int pageSize=20,DateOnly? resumeFrom=null,DateOnly? resumeTo=null,string? talentId=null,CancellationToken ct=default)=>service.GetJobRankingAsync(id,runId,page,pageSize,resumeFrom,resumeTo,talentId,ct);
    [HttpGet("{id:long}/merge-targets")] public Task<IReadOnlyList<JobMergeTargetDto>> MergeTargets(long id,CancellationToken ct)=>service.GetJobMergeTargetsAsync(id,ct);
    [HttpPost("{id:long}/merge")] public async Task<IActionResult> Merge(long id,MergeJobRequest request,CancellationToken ct){await service.MergeJobAsync(id,request,User.GetUserId(),ct);return NoContent();}
    [HttpGet("{id:long}/profile")] public async Task<ActionResult<JobProfileDto>> Profile(long id,CancellationToken ct){var item=await service.GetJobProfileAsync(id,ct);return item is null?NotFound():item;}
    [HttpPut("{id:long}/profile")]
    [HttpPost("{id:long}/profile/update")] public async Task<IActionResult> UpdateProfile(long id,SaveJobProfileRequest request,CancellationToken ct){await service.UpdateJobProfileAsync(id,request,ct);return NoContent();}
    [HttpPost("{id:long}/profile/publish")] public async Task<IActionResult> PublishProfile(long id,CancellationToken ct)
    {
        var profile=await service.GetJobProfileAsync(id,ct);
        if(profile is null)return NotFound();
        await knowledge.PublishAsync(profile.KnowledgeDocumentId,ct);
        var job=await service.GetJobAsync(id,ct);
        if(job is null)return NotFound();
        if(job.Status!="OPEN")await service.ChangeJobStatusAsync(id,"OPEN",ct);
        return Accepted();
    }
    [HttpPost("{id:long}/profile/ai-draft")] public async Task<KnowledgeAiDraftResponse> ProfileAiDraft(long id,KnowledgeAiDraftRequest request,CancellationToken ct){if(await service.GetJobProfileAsync(id,ct) is null)throw new InvalidOperationException("Job profile not found.");return await ai.GenerateAsync(request with { DocumentType="JOB_REQUIREMENT" },ct);}
}

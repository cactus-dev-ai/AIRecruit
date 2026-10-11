using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecruitAI.Application.Interviews;
using RecruitAI.Web.Infrastructure;

namespace RecruitAI.Web.Controllers;

[ApiController]
[Authorize(Policy = "Recruitment")]
[Route("api/interviews")]
public sealed class InterviewsController(IInterviewManagementService service) : ControllerBase
{
    [HttpGet] public Task<InterviewListPage> List(int page=1,int pageSize=20,string? status=null,string? keyword=null,CancellationToken ct=default)=>service.ListAsync(page,pageSize,status,keyword,ct);
    [HttpGet("candidates")] public Task<IReadOnlyList<InterviewCandidateDto>> Candidates(string? keyword=null,int take=30,CancellationToken ct=default)=>service.FindCandidatesAsync(keyword,take,ct);
    [HttpGet("plans")] public Task<IReadOnlyList<InterviewPlanDto>> Plans(long? jobId=null,CancellationToken ct=default)=>service.ListPlansAsync(jobId,ct);
    [HttpGet("plans/{id:long}")] public async Task<ActionResult<InterviewPlanDto>> Plan(long id,CancellationToken ct){var item=await service.GetPlanAsync(id,ct);return item is null?NotFound():item;}
    [HttpPost("plans")] public async Task<IActionResult> CreatePlan(SaveInterviewPlanRequest request,CancellationToken ct){var id=await service.CreatePlanAsync(request,User.GetUserId(),ct);return Created($"/api/interviews/plans/{id}",new{id});}
    [HttpPost("plans/{id:long}/update")] public async Task<IActionResult> UpdatePlan(long id,SaveInterviewPlanRequest request,CancellationToken ct){await service.UpdatePlanAsync(id,request,ct);return NoContent();}
    [HttpPost("plans/{id:long}/publish")] public async Task<IActionResult> PublishPlan(long id,CancellationToken ct){await service.PublishPlanAsync(id,User.GetUserId(),ct);return NoContent();}
    [HttpPost("plans/{id:long}/stop")] public async Task<IActionResult> StopPlan(long id,CancellationToken ct){await service.StopPlanAsync(id,ct);return NoContent();}
    [HttpPost("plans/{id:long}/clone")] public async Task<IActionResult> ClonePlan(long id,CancellationToken ct){var newId=await service.ClonePlanAsync(id,User.GetUserId(),ct);return Created($"/api/interviews/plans/{newId}",new{id=newId});}
    [HttpPost("plans/{id:long}/delete")] public async Task<IActionResult> DeletePlan(long id,CancellationToken ct){await service.DeletePlanAsync(id,ct);return NoContent();}
    [HttpPost("question-sets")] public async Task<IActionResult> CreateQuestionSet(CreateQuestionSetRequest request,CancellationToken ct){var id=await service.CreateQuestionSetDraftAsync(request,ct);return Created($"/api/interviews/question-sets/{id}",new{id});}
    [HttpGet("question-sets/{id:long}")] public async Task<ActionResult<QuestionSetDetailDto>> QuestionSet(long id,CancellationToken ct){var item=await service.GetQuestionSetAsync(id,ct);return item is null?NotFound():item;}
    [HttpPost("question-sets/{id:long}/update")] public async Task<IActionResult> UpdateQuestionSet(long id,UpdateQuestionSetRequest request,CancellationToken ct){await service.UpdateQuestionSetAsync(id,request,ct);return NoContent();}
    [HttpPost("question-sets/{id:long}/confirm")] public async Task<IActionResult> ConfirmQuestionSet(long id,CancellationToken ct){await service.ConfirmQuestionSetAsync(id,User.GetUserId(),ct);return NoContent();}
    [HttpPost("invitations")] public Task<CreatedInvitationDto> CreateInvitation(CreateInvitationRequest request,CancellationToken ct)=>service.CreateInvitationAsync(request,User.GetUserId(),ct);
    [HttpGet("invitations/{id:long}")] public async Task<ActionResult<InterviewDetailDto>> Detail(long id,CancellationToken ct){var item=await service.GetDetailAsync(id,ct);return item is null?NotFound():item;}
    [HttpPost("invitations/{id:long}/revoke")] public async Task<IActionResult> Revoke(long id,CancellationToken ct){await service.RevokeInvitationAsync(id,User.GetUserId(),ct);return NoContent();}
    [HttpPost("invitations/{id:long}/delete")] public async Task<IActionResult> Delete(long id,CancellationToken ct){await service.DeleteInvitationAsync(id,User.GetUserId(),ct);return NoContent();}
    [HttpPost("turns/{id:long}/correct")] public async Task<IActionResult> Correct(long id,CorrectTranscriptRequest request,CancellationToken ct){await service.CorrectTranscriptAsync(id,request,User.GetUserId(),ct);return NoContent();}
    [HttpPost("sessions/{id:long}/submit-report")] public async Task<IActionResult> SubmitReport(long id,CancellationToken ct){await service.SubmitReportAsync(id,User.GetUserId(),ct);return NoContent();}
    [HttpPost("sessions/{id:long}/abort")] public async Task<IActionResult> Abort(long id,[FromBody] AbortRequest request,CancellationToken ct){await service.AbortSessionAsync(id,request.Reason,User.GetUserId(),ct);return NoContent();}
    public sealed record AbortRequest(string Reason);
}

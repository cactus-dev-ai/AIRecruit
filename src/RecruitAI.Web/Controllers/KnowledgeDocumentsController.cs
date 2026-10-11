using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecruitAI.Application.Knowledge;
using RecruitAI.Web.Infrastructure;

namespace RecruitAI.Web.Controllers;

[ApiController]
[Authorize(Policy="Recruitment")]
[Route("api/knowledge-documents")]
public sealed class KnowledgeDocumentsController(IKnowledgeManagementService service, KnowledgeAiDraftService ai):ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<KnowledgeDocumentDto>> List(string? type=null,string? keyword=null,CancellationToken ct=default)=>service.ListAsync(type,keyword,ct);
    [HttpGet("{id:long}")] public async Task<ActionResult<KnowledgeDocumentDto>> Get(long id,CancellationToken ct){var item=await service.GetAsync(id,ct);return item is null?NotFound():item;}
    [HttpPost] public async Task<IActionResult> Create(SaveKnowledgeDocumentRequest request,CancellationToken ct){var id=await service.CreateAsync(request,User.GetUserId(),ct);return Created($"/api/knowledge-documents/{id}",new{id});}
    [HttpPut("{id:long}")]
    [HttpPost("{id:long}/update")] public async Task<IActionResult> Update(long id,SaveKnowledgeDocumentRequest request,CancellationToken ct){await service.UpdateAsync(id,request,ct);return NoContent();}
    [HttpPost("{id:long}/publish")] public async Task<IActionResult> Publish(long id,CancellationToken ct){await service.PublishAsync(id,ct);return Accepted();}
    [HttpPost("{id:long}/archive")] public async Task<IActionResult> Archive(long id,CancellationToken ct){await service.ArchiveAsync(id,ct);return NoContent();}
    [HttpPost("ai-draft")] public Task<KnowledgeAiDraftResponse> AiDraft(KnowledgeAiDraftRequest request,CancellationToken ct)=>ai.GenerateAsync(request,ct);
}

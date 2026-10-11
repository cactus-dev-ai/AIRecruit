using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecruitAI.Application.Knowledge;
using RecruitAI.Web.Infrastructure;

namespace RecruitAI.Web.Controllers;

[ApiController]
[Authorize(Policy = "Recruitment")]
[Route("api/knowledge-imports")]
public sealed class KnowledgeImportsController(
    IKnowledgeImportService service,
    UploadKnowledgeExcelHandler uploadHandler,
    UpdateKnowledgeImportRowHandler updateHandler,
    ConfirmKnowledgeImportHandler confirmHandler) : ControllerBase
{
    [HttpPost("excel")]
    [RequestFormLimits(MultipartBodyLengthLimit = 11_534_336)]
    [RequestSizeLimit(11_534_336)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var batchId = await uploadHandler.HandleAsync(stream, file.FileName, file.Length, User.GetUserId(), cancellationToken);
        return Created($"/api/knowledge-imports/{batchId}", new { batchId });
    }

    [HttpGet("{batchId:long}")]
    public async Task<ActionResult<KnowledgeImportBatchDto>> Get(long batchId, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(batchId, cancellationToken); return result is null ? NotFound() : result;
    }

    [HttpPatch("{batchId:long}/rows/{rowId:long}")]
    [HttpPost("{batchId:long}/rows/{rowId:long}/update")]
    public async Task<IActionResult> UpdateRow(long batchId, long rowId, UpdateKnowledgeImportRowRequest request, CancellationToken cancellationToken)
    {
        await updateHandler.HandleAsync(batchId, rowId, request, User.GetUserId(), cancellationToken); return NoContent();
    }

    [HttpPost("{batchId:long}/confirm")]
    public async Task<IActionResult> Confirm(long batchId, CancellationToken cancellationToken)
    {
        var count = await confirmHandler.HandleAsync(batchId, User.GetUserId(), cancellationToken); return Accepted(new { batchId, importedCount = count });
    }
}

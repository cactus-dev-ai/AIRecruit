using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecruitAI.Application.Common;
using RecruitAI.Application.Imports;
using RecruitAI.Web.Infrastructure;

namespace RecruitAI.Web.Controllers;

[ApiController]
[Authorize(Policy = "Recruitment")]
[Route("api/resume-imports")]
public sealed class ResumeImportsController(
    IResumeImportService service,
    RejectImportFileHandler rejectHandler,
    ConfirmImportBatchHandler confirmHandler,
    CancelImportReviewHandler cancelHandler,
    PurgeFailedImportFileHandler purgeHandler,
    ReturnFailedImportFileHandler returnHandler,
    ILogger<ResumeImportsController> logger) : ControllerBase
{
    [HttpGet]
    public Task<ResumeImportBatchPage> List([FromQuery] int page = 1, [FromQuery] int pageSize = 5, CancellationToken cancellationToken = default) =>
        service.ListAsync(page, pageSize, cancellationToken);

    [HttpPost("manual")]
    [RequestSizeLimit(220_200_960)]
    public async Task<ActionResult<ManualResumeUploadResult>> UploadManual(
        [FromForm] long jobId, [FromForm] List<IFormFile> files, CancellationToken cancellationToken)
    {
        var streams = files.Select(file => file.OpenReadStream()).ToList();
        try
        {
            var inputs = files.Select((file, index) => new ManualResumeUploadFile(file.FileName, file.Length, streams[index])).ToList();
            return Accepted(await service.UploadManualAsync(inputs, jobId, User.GetUserId(), cancellationToken));
        }
        finally
        {
            foreach (var stream in streams) await stream.DisposeAsync();
        }
    }

    [HttpGet("failed-files")]
    public Task<IReadOnlyList<ResumeImportFileDto>> Failed(CancellationToken cancellationToken) => service.ListFailedAsync(cancellationToken);

    [HttpGet("failed-files/{fileId:long}/preview")]
    public Task<ResumeImportFailurePreview> Preview(long fileId, CancellationToken cancellationToken) =>
        service.GetFailedPreviewAsync(fileId, cancellationToken);

    [HttpPost("failed-files/{fileId:long}/return")]
    public async Task<IActionResult> Return(long fileId, ReturnFailedFileRequest request, CancellationToken cancellationToken)
    {
        await returnHandler.HandleAsync(fileId, request.Reason, User.GetUserId(), cancellationToken);
        return NoContent();
    }

    [HttpPost("failed-files/{fileId:long}/purge")]
    public async Task<IActionResult> Purge(long fileId, PurgeFailedFileRequest request, CancellationToken cancellationToken)
    {
        await purgeHandler.HandleAsync(fileId, request.Reason, User.GetUserId(), cancellationToken); return NoContent();
    }

    [HttpGet("{batchId:long}")]
    public async Task<ActionResult<ResumeImportBatchDetail>> Get(long batchId, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(batchId, cancellationToken); return result is null ? NotFound() : result;
    }

    [HttpPost("{batchId:long}/files/{fileId:long}/reject")]
    public async Task<IActionResult> Reject(long batchId, long fileId, CancellationToken cancellationToken)
    {
        await rejectHandler.HandleAsync(batchId, fileId, User.GetUserId(), User.IsInRole("ADMIN"), cancellationToken); return NoContent();
    }

    [HttpPost("{batchId:long}/confirm")]
    public async Task<IActionResult> Confirm(long batchId, ConfirmResumeImportRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var result = await confirmHandler.HandleAsync(batchId, request, userId, User.IsInRole("ADMIN"), cancellationToken);

        ResumeScanResult? next = null;
        string? nextScanWarning = null;
        try
        {
            // The confirmed batch is already represented by WAITING AI tasks. Claim any JSON
            // files staged while HR was reviewing it so the next batch can be reviewed at once.
            next = await service.ScanAsync(userId, cancellationToken);
        }
        catch (BusinessException ex) when (ex.Code == "IMPORT_REVIEW_PENDING")
        {
            nextScanWarning = ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import batch {BatchId} was queued, but scanning the next staged batch failed.", batchId);
            nextScanWarning = "当前批次已加入AI任务；下一批自动扫描失败，可稍后重新触发上传扫描。";
        }

        return Accepted(new
        {
            result.BatchId,
            result.TaskCount,
            NextBatchId = next?.BatchId,
            NextReviewReadyCount = next?.ReviewReadyCount ?? 0,
            NextScanMessage = next?.Message ?? nextScanWarning
        });
    }

    [HttpPost("{batchId:long}/cancel-review")]
    public async Task<IActionResult> Cancel(long batchId, CancellationToken cancellationToken)
    {
        await cancelHandler.HandleAsync(batchId, User.GetUserId(), User.IsInRole("ADMIN"), cancellationToken); return NoContent();
    }
}

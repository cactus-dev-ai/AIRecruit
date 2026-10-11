using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using RecruitAI.Application.Imports;
using RecruitAI.Web.Controllers;

namespace RecruitAI.Tests;

public sealed class ResumeImportQueueFlowTests
{
    [Fact]
    public async Task Confirm_queues_current_batch_and_scans_next_batch_immediately()
    {
        var service = new FakeResumeImportService();
        var controller = CreateController(service);

        var action = await controller.Confirm(7, new ConfirmResumeImportRequest([], null, "AA=="), CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(action);
        Assert.NotNull(accepted.Value);
        Assert.Equal(1, service.ConfirmCalls);
        Assert.Equal(1, service.ScanCalls);
        Assert.Equal(8L, ReadProperty<long?>(accepted.Value, "NextBatchId"));
        Assert.Equal(3, ReadProperty<int>(accepted.Value, "TaskCount"));
    }

    private static ResumeImportsController CreateController(IResumeImportService service)
    {
        var controller = new ResumeImportsController(
            service,
            new RejectImportFileHandler(service),
            new ConfirmImportBatchHandler(service),
            new CancelImportReviewHandler(service),
            new PurgeFailedImportFileHandler(service),
            new ReturnFailedImportFileHandler(service),
            NullLogger<ResumeImportsController>.Instance);
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "5"), new Claim(ClaimTypes.Role, "HR")],
            "test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return controller;
    }

    private static T ReadProperty<T>(object value, string name) =>
        (T)(value.GetType().GetProperty(name)!.GetValue(value)!);

    private sealed class FakeResumeImportService : IResumeImportService
    {
        public int ConfirmCalls { get; private set; }
        public int ScanCalls { get; private set; }

        public Task<ConfirmResumeImportResult> ConfirmAsync(long batchId, ConfirmResumeImportRequest request, long userId, bool isAdmin, CancellationToken cancellationToken)
        {
            ConfirmCalls++;
            return Task.FromResult(new ConfirmResumeImportResult(batchId, 3));
        }

        public Task<ResumeScanResult> ScanAsync(long userId, CancellationToken cancellationToken)
        {
            ScanCalls++;
            return Task.FromResult(new ResumeScanResult("BATCH_CREATED", 8, 2, 2, 0, 0, 2, false, "下一批已建立。"));
        }

        public Task<ManualResumeUploadResult> UploadManualAsync(IReadOnlyList<ManualResumeUploadFile> files, long jobId, long userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ResumeImportBatchPage> ListAsync(int page, int pageSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ResumeImportBatchDetail?> GetAsync(long batchId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RejectAsync(long batchId, long fileId, long userId, bool isAdmin, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelReviewAsync(long batchId, long userId, bool isAdmin, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ResumeImportFileDto>> ListFailedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ResumeImportFailurePreview> GetFailedPreviewAsync(long fileId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReturnFailedAsync(long fileId, string reason, long userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task PurgeFailedAsync(long fileId, string reason, long userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

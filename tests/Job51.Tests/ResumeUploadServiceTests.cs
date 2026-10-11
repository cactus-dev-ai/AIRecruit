using Job51.WinForms;

namespace Job51.Tests;

public sealed class ResumeUploadServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Job51.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CleanupCompletedBatchIndexes_RemovesIndexAndEmptyBatch()
    {
        var batch = Path.Combine(_root, "20260812_120000_人才搜索");
        Directory.CreateDirectory(batch);
        File.WriteAllText(Path.Combine(batch, "index.json"), "{}");

        var cleaned = ResumeUploadService.CleanupCompletedBatchIndexes(_root, _ => { });

        Assert.Equal(1, cleaned);
        Assert.False(Directory.Exists(batch));
    }

    [Fact]
    public void CleanupCompletedBatchIndexes_KeepsIndexWhileResumeIsPending()
    {
        var batch = Path.Combine(_root, "20260812_120001_人才搜索");
        Directory.CreateDirectory(batch);
        var index = Path.Combine(batch, "index.json");
        File.WriteAllText(index, "{}");
        File.WriteAllText(Path.Combine(batch, "candidate_001_test.json"), "{}");

        var cleaned = ResumeUploadService.CleanupCompletedBatchIndexes(_root, _ => { });

        Assert.Equal(0, cleaned);
        Assert.True(File.Exists(index));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

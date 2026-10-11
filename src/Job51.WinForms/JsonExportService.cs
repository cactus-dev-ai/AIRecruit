using System.Text;
using System.Text.Json;

namespace Job51.WinForms;

internal sealed class JsonExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public string RootDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "ResumeTemp");

    public string CreateBatchDirectory(string kind)
    {
        Directory.CreateDirectory(RootDirectory);
        var safeKind = SanitizeFileName(kind);
        var folder = Path.Combine(RootDirectory, $"{DateTime.Now:yyyyMMdd_HHmmss}_{safeKind}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public async Task<string> SaveCandidateAsync(
        string batchDirectory,
        CandidatePageData candidate,
        int sequence,
        CancellationToken cancellationToken)
    {
        var cleaned = ResumeTextCleaner.CleanCandidate(candidate.CandidateName, candidate.VisibleText);
        candidate.CandidateName = cleaned.CandidateName;
        candidate.VisibleText = cleaned.VisibleText;
        var name = string.IsNullOrWhiteSpace(candidate.CandidateName)
            ? "candidate"
            : SanitizeFileName(candidate.CandidateName);
        var path = Path.Combine(batchDirectory, $"candidate_{sequence:000}_{name}.json");
        await WriteJsonAsync(path, candidate, cancellationToken);
        return path;
    }

    public async Task<string> SaveManagementApiResumeAsync(
        string batchDirectory,
        ManagementApiResumeData candidate,
        int sequence,
        CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(candidate.CandidateName)
            ? "candidate"
            : SanitizeFileName(candidate.CandidateName);
        var path = Path.Combine(batchDirectory, $"candidate_{sequence:000}_{name}.json");
        await WriteJsonAsync(path, candidate, cancellationToken);
        return path;
    }

    public Task SaveIndexAsync(string batchDirectory, BatchIndex index, CancellationToken cancellationToken)
        => WriteJsonAsync(Path.Combine(batchDirectory, "index.json"), index, cancellationToken);

    public Task SavePageFallbackAsync(
        string batchDirectory,
        CandidatePageData page,
        CancellationToken cancellationToken)
        => WriteJsonAsync(Path.Combine(batchDirectory, "current_page.json"), page, cancellationToken);

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), cancellationToken);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars);
        return result.Length > 40 ? result[..40] : result;
    }
}

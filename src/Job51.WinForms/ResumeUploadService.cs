using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Job51.WinForms;

internal sealed class ResumeUploadService
{
    private readonly ServerUploadSettings _settings;

    public ResumeUploadService(ServerUploadSettings settings)
    {
        _settings = settings;
    }

    public async Task<ResumeUploadSummary> UploadDirectoryAsync(
        string rootDirectory,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(rootDirectory);
        var archiveDirectory = Path.Combine(AppContext.BaseDirectory, "Uploaded");
        CleanupCompletedBatchIndexes(rootDirectory, log);
        var files = Directory.EnumerateFiles(rootDirectory, "candidate_*.json", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(archiveDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(_settings.MaximumFilesPerRun, 1, 1000))
            .ToArray();
        if (files.Length == 0) return new ResumeUploadSummary(0, 0, 0, 0, 0, "没有待上传文件。");

        var manifestPath = Path.Combine(rootDirectory, ".uploaded.sha256");
        var uploadedHashes = File.Exists(manifestPath)
            ? new HashSet<string>(await File.ReadAllLinesAsync(manifestPath, cancellationToken), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(_settings.TimeoutSeconds, 10, 300))
        };
        client.DefaultRequestHeaders.Add("X-API-Key", _settings.ApiKey);

        var uploaded = 0;
        var duplicates = 0;
        var failed = 0;
        var cleanupFailed = 0;
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await CleanVisibleTextBeforeUploadAsync(path, cancellationToken);
                string hash;
                await using (var hashStream = File.OpenRead(path))
                    hash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, cancellationToken)).ToLowerInvariant();

                if (uploadedHashes.Contains(hash))
                {
                    duplicates++;
                    log($"本地记录确认已上传：{Path.GetFileName(path)}");
                    if (!TryArchiveUploadedJson(path, rootDirectory, archiveDirectory, log)) cleanupFailed++;
                    continue;
                }

                bool serverDuplicate;
                string? serverAction = null;
                await using (var stream = File.OpenRead(path))
                {
                    using var content = new StreamContent(stream);
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    using var response = await client.PostAsync(_settings.ApiUrl, content, cancellationToken);
                    var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        failed++;
                        log($"上传失败：{Path.GetFileName(path)}，HTTP {(int)response.StatusCode} {responseText}");
                        continue;
                    }

                    var result = JsonSerializer.Deserialize<ResumeUploadResponse>(responseText,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    serverDuplicate = result?.Duplicate == true;
                    serverAction = result?.Action;
                }

                if (serverDuplicate)
                {
                    duplicates++;
                    log($"服务端无需上传（{serverAction ?? "DUPLICATE"}）：{Path.GetFileName(path)}");
                }
                else
                {
                    uploaded++;
                    log($"上传成功：{Path.GetFileName(path)}");
                }

                uploadedHashes.Add(hash);
                await File.AppendAllTextAsync(manifestPath, hash + Environment.NewLine, new UTF8Encoding(false), cancellationToken);
                if (!TryArchiveUploadedJson(path, rootDirectory, archiveDirectory, log)) cleanupFailed++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                log($"上传失败：{Path.GetFileName(path)}，{ex.Message}");
            }
        }

        var scanMessage = uploaded > 0
            ? await TriggerServerScanAsync(client, cancellationToken)
            : "本次没有新增临时简历，无需扫描。";
        CleanupCompletedBatchIndexes(rootDirectory, log);
        log($"服务端扫描：{scanMessage}");
        return new ResumeUploadSummary(files.Length, uploaded, duplicates, failed, cleanupFailed, scanMessage);
    }

    internal static int CleanupCompletedBatchIndexes(string rootDirectory, Action<string> log)
    {
        if (!Directory.Exists(rootDirectory)) return 0;

        var cleaned = 0;
        foreach (var indexPath in Directory.EnumerateFiles(rootDirectory, "index.json", SearchOption.AllDirectories).ToArray())
        {
            var batchDirectory = Path.GetDirectoryName(indexPath);
            if (string.IsNullOrWhiteSpace(batchDirectory)) continue;

            // A batch index remains useful while even one resume is waiting for upload.
            // Once all candidate JSON files have been archived, the index is only stale
            // download metadata and the now-empty batch directory can be removed.
            if (Directory.EnumerateFiles(batchDirectory, "candidate_*.json", SearchOption.TopDirectoryOnly).Any())
                continue;

            try
            {
                File.Delete(indexPath);
                cleaned++;
                if (!Directory.EnumerateFileSystemEntries(batchDirectory).Any())
                    Directory.Delete(batchDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log($"清理已完成批次索引失败：{indexPath}，{ex.Message}");
            }
        }

        if (cleaned > 0) log($"已清理 {cleaned} 个完成批次的 index.json。");
        return cleaned;
    }

    private async Task<string> TriggerServerScanAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var scanUrl = _settings.ApiUrl.TrimEnd('/') + "/scan";
        using var response = await client.PostAsync(scanUrl, new StringContent(string.Empty, Encoding.UTF8, "application/json"), cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<ResumeScanResponse>(responseText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (response.IsSuccessStatusCode)
            return result?.Message ?? "已完成扫描。";
        if ((int)response.StatusCode == 409 && string.Equals(result?.Code, "IMPORT_REVIEW_PENDING", StringComparison.OrdinalIgnoreCase))
            return result?.Message ?? "已有待审阅批次，本次文件保留在临时目录。";
        return $"扫描请求失败：HTTP {(int)response.StatusCode}";
    }

    private static async Task CleanVisibleTextBeforeUploadAsync(string path, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("visible_text", out var visibleText) ||
            visibleText.ValueKind != JsonValueKind.String)
            return;
        var original = visibleText.GetString() ?? string.Empty;
        var originalName = document.RootElement.TryGetProperty("candidate_name", out var candidateName) &&
                           candidateName.ValueKind == JsonValueKind.String
            ? candidateName.GetString() ?? string.Empty
            : string.Empty;
        var cleaned = ResumeTextCleaner.CleanCandidate(originalName, original);
        if (string.Equals(original, cleaned.VisibleText, StringComparison.Ordinal) &&
            string.Equals(originalName, cleaned.CandidateName, StringComparison.Ordinal)) return;

        using var output = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("visible_text")) writer.WriteString("visible_text", cleaned.VisibleText);
                else if (property.NameEquals("candidate_name")) writer.WriteString("candidate_name", cleaned.CandidateName);
                else property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        var temporary = path + ".cleaning";
        await File.WriteAllBytesAsync(temporary, output.ToArray(), cancellationToken);
        File.Move(temporary, path, true);
    }

    private static bool TryArchiveUploadedJson(string path, string rootDirectory, string archiveDirectory, Action<string> log)
    {
        try
        {
            var relative = Path.GetRelativePath(rootDirectory, path);
            var target = Path.Combine(archiveDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target)) File.Delete(target);
            File.Move(path, target);
            log($"已归档本地完整简历：{Path.GetFileName(path)}");
            return true;
        }
        catch (Exception ex)
        {
            log($"服务端已处理，但本地归档失败：{Path.GetFileName(path)}，{ex.Message}");
            return false;
        }
    }

    private sealed class ResumeUploadResponse
    {
        public bool Duplicate { get; set; }
        public string? Action { get; set; }
    }

    private sealed class ResumeScanResponse
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
    }
}

internal sealed record ResumeUploadSummary(int Total, int Uploaded, int Duplicates, int Failed, int CleanupFailed, string ScanMessage);

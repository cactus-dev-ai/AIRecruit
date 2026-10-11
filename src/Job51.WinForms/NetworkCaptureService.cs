using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace Job51.WinForms;

internal sealed class NetworkCaptureService : IDisposable
{
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization", "cookie", "set-cookie", "token", "access_token", "refresh_token",
        "password", "passwd", "secret", "session", "sessionid", "ticket"
    };

    private readonly NetworkCaptureSettings _settings;
    private readonly DiagnosticLogService _diagnostics;
    private readonly bool _excludeViewedCandidates;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
    private readonly JsonSerializerOptions _jsonLineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _snapshotSync = new();
    private CoreWebView2? _browser;
    private CoreWebView2DevToolsProtocolEventReceiver? _requestReceiver;
    private int _sequence;
    private bool _disposed;
    private ManagementApiSnapshot? _latestManagementSnapshot;
    private TalentJobApiSnapshot? _latestTalentJobSnapshot;
    private TalentSearchApiSnapshot? _latestTalentSearchSnapshot;

    public NetworkCaptureService(
        NetworkCaptureSettings settings,
        DiagnosticLogService diagnostics,
        bool excludeViewedCandidates = true)
    {
        _settings = settings;
        _diagnostics = diagnostics;
        _excludeViewedCandidates = excludeViewedCandidates;
        CaptureDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "NetworkCapture",
            DateTime.Now.ToString("yyyyMMdd_HHmmss"));
    }

    public string CaptureDirectory { get; }

    public async Task StartAsync(CoreWebView2 browser)
    {
        if (!_settings.Enabled || _browser is not null) return;

        Directory.CreateDirectory(CaptureDirectory);
        _browser = browser;
        _browser.WebResourceResponseReceived += Browser_WebResourceResponseReceived;
        _requestReceiver = browser.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
        _requestReceiver.DevToolsProtocolEventReceived += RequestReceiver_DevToolsProtocolEventReceived;
        await browser.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
        _diagnostics.Write("network_capture_started", new { directory = CaptureDirectory });
    }

    private async void RequestReceiver_DevToolsProtocolEventReceived(
        object? sender,
        CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.ParameterObjectAsJson);
            if (!document.RootElement.TryGetProperty("request", out var request)) return;
            var url = request.TryGetProperty("url", out var urlValue) ? urlValue.GetString() : null;
            if (!ShouldCapture(url)) return;

            var method = request.TryGetProperty("method", out var methodValue)
                ? methodValue.GetString()
                : null;
            var postData = request.TryGetProperty("postData", out var postDataValue)
                ? postDataValue.GetString()
                : null;
            await AppendJsonLineAsync("requests.jsonl", new
            {
                timestamp = DateTimeOffset.Now,
                method,
                url = SanitizeUrl(url!),
                route = new Uri(url!).AbsolutePath,
                request_body = SanitizeBody(postData)
            });
        }
        catch (Exception ex)
        {
            _diagnostics.Write("network_request_capture_failed", new { error_type = ex.GetType().Name });
        }
    }

    private async void Browser_WebResourceResponseReceived(
        object? sender,
        CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            var url = e.Request.Uri;
            if (!ShouldCapture(url)) return;

            var response = e.Response;
            var contentType = TryGetHeader(response.Headers, "Content-Type");
            if (!IsJsonContentType(contentType)) return;

            await using var stream = await response.GetContentAsync();
            if (stream is null) return;
            var body = await ReadLimitedAsync(stream, Math.Max(1024, _settings.MaximumBodyBytes));
            if (string.IsNullOrWhiteSpace(body)) return;

            if (uriPathEquals(url, "/resume/administer/talent_pool_resume"))
                CaptureManagementSnapshot(body);
            if (uriPathEquals(url, "/resume/search/get_combine_joblist") ||
                uriPathEquals(url, "/jobj/list/get_topfive_list"))
                CaptureTalentJobSnapshot(new Uri(url).AbsolutePath, body);
            if (uriPathEquals(url, "/resume/commercialize/talent_recommend_resume_list") ||
                uriPathEquals(url, "/resume/search/talent_hunt_resume_list"))
                CaptureTalentSearchSnapshot(new Uri(url).AbsolutePath, body);

            var sequence = Interlocked.Increment(ref _sequence);
            var uri = new Uri(url);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..10].ToLowerInvariant();
            var safeRoute = string.Join('_', uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .TakeLast(3)
                .Select(SanitizeFilePart));
            if (string.IsNullOrWhiteSpace(safeRoute)) safeRoute = "response";
            var fileName = $"{sequence:0000}_{safeRoute}_{hash}.json";

            var record = new
            {
                timestamp = DateTimeOffset.Now,
                request = new
                {
                    method = e.Request.Method,
                    url = SanitizeUrl(url),
                    route = uri.AbsolutePath
                },
                response = new
                {
                    status_code = response.StatusCode,
                    reason_phrase = response.ReasonPhrase,
                    content_type = contentType,
                    body = SanitizeBody(body)
                }
            };
            await WriteJsonAsync(Path.Combine(CaptureDirectory, fileName), record);
            _diagnostics.Write("network_response_captured", new
            {
                route = uri.AbsolutePath,
                status_code = response.StatusCode,
                file_name = fileName
            });
        }
        catch (Exception ex)
        {
            _diagnostics.Write("network_response_capture_failed", new { error_type = ex.GetType().Name });
        }
    }

    public async Task<ManagementApiSnapshot?> WaitForManagementSnapshotAsync(
        DateTimeOffset notBefore,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.Now.Add(timeout);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_snapshotSync)
            {
                if (_latestManagementSnapshot is { } snapshot && snapshot.CapturedAt >= notBefore)
                    return snapshot;
            }
            await Task.Delay(200, cancellationToken);
        } while (DateTimeOffset.Now < deadline);
        return null;
    }

    public async Task<TalentJobApiSnapshot?> WaitForTalentJobSnapshotAsync(
        DateTimeOffset notBefore,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string? requiredRoute = null)
    {
        var deadline = DateTimeOffset.Now.Add(timeout);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_snapshotSync)
            {
                if (_latestTalentJobSnapshot is { } snapshot && snapshot.CapturedAt >= notBefore &&
                    (string.IsNullOrWhiteSpace(requiredRoute) ||
                     snapshot.SourceRoute.Equals(requiredRoute, StringComparison.OrdinalIgnoreCase)))
                    return snapshot;
            }
            await Task.Delay(200, cancellationToken);
        } while (DateTimeOffset.Now < deadline);
        return null;
    }

    public async Task<TalentSearchApiSnapshot?> WaitForTalentSearchSnapshotAsync(
        DateTimeOffset notBefore,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.Now.Add(timeout);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_snapshotSync)
            {
                if (_latestTalentSearchSnapshot is { } snapshot && snapshot.CapturedAt >= notBefore)
                    return snapshot;
            }
            await Task.Delay(200, cancellationToken);
        } while (DateTimeOffset.Now < deadline);
        return null;
    }

    private void CaptureTalentJobSnapshot(string sourceRoute, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data))
                return;
            JsonElement list;
            if (!data.TryGetProperty("my_job_list", out list) &&
                !data.TryGetProperty("joblist", out list))
                return;
            if (list.ValueKind != JsonValueKind.Array) return;

            var jobs = list.EnumerateArray()
                .Select(item => new TalentJobApiData
                {
                    JobId = ReadString(item, "jobid"),
                    JobName = ReadString(item, "job_name"),
                    FunctionTypeCode = ReadString(item, "funtype_code"),
                    Status = ReadString(item, "status"),
                    PublishStatus = ReadString(item, "publish_status"),
                    AiKeyword = ReadString(item, "ai_keyword")
                })
                .Where(job => !string.IsNullOrWhiteSpace(job.JobId) &&
                              !string.IsNullOrWhiteSpace(job.JobName) &&
                              !job.JobName.Equals("不限职位", StringComparison.OrdinalIgnoreCase) &&
                              job.Status.Equals("24", StringComparison.OrdinalIgnoreCase) &&
                              job.PublishStatus.Equals("发布中", StringComparison.OrdinalIgnoreCase))
                .GroupBy(job => job.JobId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (jobs.Count == 0) return;

            var snapshot = new TalentJobApiSnapshot(DateTimeOffset.Now, sourceRoute, jobs);
            lock (_snapshotSync)
                _latestTalentJobSnapshot = snapshot;
            _diagnostics.Write("talent_job_api_snapshot_captured", new { source_route = sourceRoute, job_count = jobs.Count });
        }
        catch (Exception ex)
        {
            _diagnostics.Write("talent_job_api_snapshot_failed", new { error_type = ex.GetType().Name });
        }
    }

    private void CaptureTalentSearchSnapshot(string route, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data)) return;
            var candidates = new List<TalentSearchApiCandidate>();
            if (data.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (_excludeViewedCandidates && ReadString(item, "is_read") == "1") continue;
                    var talentId = FirstNonEmpty(ReadString(item, "real_userid"), ReadString(item, "userid"));
                    if (string.IsNullOrWhiteSpace(talentId)) continue;
                    var candidateName = string.Empty;
                    if (item.TryGetProperty("base_info", out var baseInfo))
                        candidateName = ReadString(baseInfo, "resume_name");
                    candidates.Add(new TalentSearchApiCandidate(talentId, candidateName));
                }
            }
            candidates = candidates
                .GroupBy(candidate => candidate.TalentId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            var pageCount = candidates.Count;
            var total = ReadInt(data, "total");
            var requestId = FirstNonEmpty(ReadString(data, "requestid"), ReadString(data, "requestId"));
            var jobId = FirstNonEmpty(ReadString(data, "jobid"), ReadString(data, "jobId"));
            var snapshot = new TalentSearchApiSnapshot(
                DateTimeOffset.Now, route, pageCount, total, requestId, jobId, candidates);
            lock (_snapshotSync)
                _latestTalentSearchSnapshot = snapshot;
            _diagnostics.Write("talent_search_api_snapshot_captured", new
            {
                route,
                current_page_count = pageCount,
                total_count = total,
                exclude_viewed = _excludeViewedCandidates
            });
        }
        catch (Exception ex)
        {
            _diagnostics.Write("talent_search_api_snapshot_failed", new { error_type = ex.GetType().Name });
        }
    }

    private void CaptureManagementSnapshot(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("list", out var list) ||
                list.ValueKind != JsonValueKind.Array)
                return;

            var newResumes = new List<ManagementApiResumeData>();
            foreach (var item in list.EnumerateArray())
            {
                if (ReadString(item, "is_read") != "0") continue;
                var talentId = FirstNonEmpty(
                    ReadString(item, "real_userid"),
                    ReadString(item, "userid"),
                    ReadString(item, "cvlogid"));
                if (string.IsNullOrWhiteSpace(talentId)) continue;

                string? candidateName = null;
                if (item.TryGetProperty("base_info", out var baseInfo))
                    candidateName = ReadString(baseInfo, "resume_name");
                newResumes.Add(new ManagementApiResumeData
                {
                    TalentId = talentId,
                    CandidateName = candidateName,
                    ApiPayload = item.Clone(),
                    DownloadedAt = DateTimeOffset.Now
                });
            }

            var snapshot = new ManagementApiSnapshot(DateTimeOffset.Now, list.GetArrayLength(), newResumes);
            lock (_snapshotSync)
                _latestManagementSnapshot = snapshot;
            _diagnostics.Write("management_api_snapshot_captured", new
            {
                current_page_count = snapshot.CurrentPageCount,
                new_count = snapshot.NewResumes.Count
            });
        }
        catch (Exception ex)
        {
            _diagnostics.Write("management_api_snapshot_failed", new { error_type = ex.GetType().Name });
        }
    }

    private static bool uriPathEquals(string value, string expected) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.AbsolutePath.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
    }

    private static int ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : 0;
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private bool ShouldCapture(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (!_settings.AllowedHosts.Any(host =>
                uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith('.' + host, StringComparison.OrdinalIgnoreCase)))
            return false;
        return _settings.UrlKeywords.Length == 0 ||
               _settings.UrlKeywords.Any(keyword =>
                   uri.PathAndQuery.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryGetHeader(CoreWebView2HttpResponseHeaders headers, string name)
    {
        try { return headers.GetHeader(name); }
        catch { return null; }
    }

    private static bool IsJsonContentType(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("text/json", StringComparison.OrdinalIgnoreCase));

    private static async Task<string> ReadLimitedAsync(Stream stream, int maximumBytes)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (buffer.Length < maximumBytes)
        {
            var requested = (int)Math.Min(chunk.Length, maximumBytes - buffer.Length);
            var read = await stream.ReadAsync(chunk.AsMemory(0, requested));
            if (read == 0) break;
            await buffer.WriteAsync(chunk.AsMemory(0, read));
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static object? SanitizeBody(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var node = JsonNode.Parse(value);
            if (node is null) return null;
            Redact(node);
            return node;
        }
        catch
        {
            var safeValue = RedactPlainText(value);
            return safeValue.Length <= 20000 ? safeValue : safeValue[..20000] + "…[truncated]";
        }
    }

    private static string RedactPlainText(string value)
    {
        foreach (var name in SensitiveNames)
        {
            value = Regex.Replace(
                value,
                $@"(?i)(\b{Regex.Escape(name)}\b\s*[=:]\s*)([^&\s,}}]+)",
                "$1[REDACTED]");
            value = Regex.Replace(
                value,
                $@"(?i)([""']{Regex.Escape(name)}[""']\s*:\s*[""'])(.*?)([""'])",
                "$1[REDACTED]$3");
        }
        return value;
    }

    private static void Redact(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj.ToList())
            {
                if (SensitiveNames.Contains(pair.Key) ||
                    SensitiveNames.Any(name => pair.Key.Contains(name, StringComparison.OrdinalIgnoreCase)))
                    obj[pair.Key] = "[REDACTED]";
                else if (pair.Value is not null)
                    Redact(pair.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
                if (item is not null) Redact(item);
        }
    }

    private static string SanitizeUrl(string value)
    {
        var uri = new Uri(value);
        var queryNames = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => Uri.UnescapeDataString(part.Split('=', 2)[0]))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
        var query = string.Join('&', queryNames.Select(name => Uri.EscapeDataString(name) + "=[REDACTED]"));
        return uri.GetLeftPart(UriPartial.Path) + (query.Length == 0 ? string.Empty : "?" + query);
    }

    private static string SanitizeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars).Trim('_');
    }

    private async Task AppendJsonLineAsync(string fileName, object value)
    {
        var line = JsonSerializer.Serialize(value, _jsonLineOptions) + Environment.NewLine;
        await _writeLock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(
                Path.Combine(CaptureDirectory, fileName),
                line,
                new UTF8Encoding(false));
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task WriteJsonAsync(string path, object value)
    {
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        await _writeLock.WaitAsync();
        try { await File.WriteAllTextAsync(path, json, new UTF8Encoding(false)); }
        finally { _writeLock.Release(); }
    }

    public async Task<int> ClearStoredFilesAsync(CancellationToken cancellationToken = default)
    {
        var root = Path.GetDirectoryName(CaptureDirectory);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return 0;

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var deleted = 0;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch
                {
                    // Diagnostic cleanup must not block normal resume uploads.
                }
            }

            foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length)
                         .ToArray())
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory);
                }
                catch
                {
                    // Keep a directory if another event is still using it.
                }
            }
            Directory.CreateDirectory(CaptureDirectory);
            return deleted;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_browser is not null)
            _browser.WebResourceResponseReceived -= Browser_WebResourceResponseReceived;
        if (_requestReceiver is not null)
            _requestReceiver.DevToolsProtocolEventReceived -= RequestReceiver_DevToolsProtocolEventReceived;
        _writeLock.Dispose();
    }
}

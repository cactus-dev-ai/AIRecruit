using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RecruitAI.Infrastructure.Interviews;

public sealed record TencentInterviewSettings(
    bool Enabled, string SecretId, string SecretKey, int SdkAppId, string SdkSecretKey,
    string CosBucket, string CosRegion, string AsrEngineModelType, int TokenMinutes = 120);

public sealed class TencentInterviewGateway(HttpClient http, TencentInterviewSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = null };

    public bool Enabled => settings.Enabled;
    public int SdkAppId => settings.SdkAppId;

    public string CreateUserSig(string userId, int expiresSeconds = 7200)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var source = $"TLS.identifier:{userId}\nTLS.sdkappid:{settings.SdkAppId}\nTLS.time:{now}\nTLS.expire:{expiresSeconds}\n";
        var sig = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.SdkSecretKey), Encoding.UTF8.GetBytes(source)));
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["TLS.ver"] = "2.0", ["TLS.identifier"] = userId, ["TLS.sdkappid"] = settings.SdkAppId,
            ["TLS.expire"] = expiresSeconds, ["TLS.time"] = now, ["TLS.sig"] = sig
        });
        using var output = new MemoryStream();
        using (var zip = new ZLibStream(output, CompressionLevel.SmallestSize, true)) zip.Write(payload);
        return Convert.ToBase64String(output.ToArray()).Replace('+', '*').Replace('/', '-').Replace('=', '_');
    }

    public async Task<(string TaskId, string ProviderSessionId)> StartRecordingAsync(string roomId, string robotUserId, string robotSig, CancellationToken ct)
    {
        EnsureEnabled();
        var providerSessionId = Guid.NewGuid().ToString("N");
        var body = new
        {
            SdkAppId = settings.SdkAppId, RoomId = roomId, RoomIdType = 0, UserId = robotUserId, UserSig = robotSig,
            RecordParams = new { RecordMode = 1, MaxIdleTime = 60, StreamType = 1 },
            StorageParams = new
            {
                CloudStorage = new { Vendor = 0, Region = settings.CosRegion, Bucket = settings.CosBucket, AccessKey = settings.SecretId, SecretKey = settings.SecretKey }
            }
        };
        using var json = await SendTencentAsync("trtc", "trtc.tencentcloudapi.com", "2019-07-22", "CreateCloudRecording", body, ct);
        ThrowTencentError(json);
        var response = json.RootElement.GetProperty("Response");
        return (response.GetProperty("TaskId").GetString()!, providerSessionId);
    }

    public async Task StopRecordingAsync(string roomId, string robotUserId, string robotSig, string taskId, CancellationToken ct)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(taskId)) return;
        var body = new { SdkAppId = settings.SdkAppId, RoomId = roomId, RoomIdType = 0, UserId = robotUserId, UserSig = robotSig, TaskId = taskId };
        using var json = await SendTencentAsync("trtc", "trtc.tencentcloudapi.com", "2019-07-22", "DeleteCloudRecording", body, ct);
        var response = json.RootElement.GetProperty("Response");
        if (response.TryGetProperty("Error", out var error) && error.GetProperty("Code").GetString() is not "ResourceNotFound") ThrowTencentError(json);
    }

    public async Task<string> UploadAndRecognizeAsync(string objectKey, byte[] wav, CancellationToken ct)
    {
        EnsureEnabled();
        await PutCosAsync(objectKey, wav, "audio/wav", ct);
        var body = new
        {
            ProjectId = 0, SubServiceType = 2, EngSerViceType = settings.AsrEngineModelType,
            SourceType = 1, VoiceFormat = "wav", Data = Convert.ToBase64String(wav), DataLen = wav.Length,
            WordInfo = 0, FilterDirty = 1, FilterModal = 1, FilterPunc = 0, ConvertNumMode = 1
        };
        using var json = await SendTencentAsync("asr", "asr.tencentcloudapi.com", "2019-06-14", "SentenceRecognition", body, ct);
        ThrowTencentError(json);
        return json.RootElement.GetProperty("Response").GetProperty("Result").GetString()?.Trim() ?? string.Empty;
    }

    private async Task PutCosAsync(string objectKey, byte[] data, string contentType, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var keyTime = $"{now};{now + 600}";
        var path = "/" + string.Join('/', objectKey.Split('/').Select(Uri.EscapeDataString));
        var host = $"{settings.CosBucket}.cos.{settings.CosRegion}.myqcloud.com";
        var httpString = $"put\n{path}\n\nhost={host}\n";
        var signKey = HmacSha1Hex(Encoding.UTF8.GetBytes(settings.SecretKey), keyTime);
        var stringToSign = $"sha1\n{keyTime}\n{Sha1Hex(httpString)}\n";
        var signature = HmacSha1Hex(Encoding.UTF8.GetBytes(signKey), stringToSign);
        var auth = $"q-sign-algorithm=sha1&q-ak={Uri.EscapeDataString(settings.SecretId)}&q-sign-time={keyTime}&q-key-time={keyTime}&q-header-list=host&q-url-param-list=&q-signature={signature}";
        using var request = new HttpRequestMessage(HttpMethod.Put, $"https://{host}{path}");
        request.Headers.TryAddWithoutValidation("Authorization", auth);
        request.Headers.Host = host;
        request.Content = new ByteArrayContent(data);
        request.Content.Headers.ContentType = new(contentType);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<JsonDocument> SendTencentAsync(string service, string host, string version, string action, object body, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(body, JsonOptions);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var date = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var canonicalHeaders = $"content-type:application/json; charset=utf-8\nhost:{host}\nx-tc-action:{action.ToLowerInvariant()}\n";
        const string signedHeaders = "content-type;host;x-tc-action";
        var canonicalRequest = $"POST\n/\n\n{canonicalHeaders}\n{signedHeaders}\n{Sha256Hex(payload)}";
        var scope = $"{date}/{service}/tc3_request";
        var stringToSign = $"TC3-HMAC-SHA256\n{timestamp}\n{scope}\n{Sha256Hex(canonicalRequest)}";
        var secretDate = HmacSha256(Encoding.UTF8.GetBytes("TC3" + settings.SecretKey), date);
        var secretService = HmacSha256(secretDate, service);
        var secretSigning = HmacSha256(secretService, "tc3_request");
        var signature = Convert.ToHexString(HmacSha256(secretSigning, stringToSign)).ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/");
        request.Headers.TryAddWithoutValidation("Authorization", $"TC3-HMAC-SHA256 Credential={settings.SecretId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
        request.Headers.TryAddWithoutValidation("Host", host);
        request.Headers.TryAddWithoutValidation("X-TC-Action", action);
        request.Headers.TryAddWithoutValidation("X-TC-Version", version);
        request.Headers.TryAddWithoutValidation("X-TC-Timestamp", timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-TC-Region", settings.CosRegion);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(content);
    }

    private static void ThrowTencentError(JsonDocument json)
    {
        var response = json.RootElement.GetProperty("Response");
        if (!response.TryGetProperty("Error", out var error)) return;
        throw new InvalidOperationException($"Tencent {error.GetProperty("Code").GetString()}: {error.GetProperty("Message").GetString()}");
    }

    private void EnsureEnabled()
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.SecretId) || string.IsNullOrWhiteSpace(settings.SecretKey) || settings.SdkAppId <= 0 || string.IsNullOrWhiteSpace(settings.SdkSecretKey))
            throw new InvalidOperationException("Tencent interview integration is not configured.");
    }

    private static byte[] HmacSha256(byte[] key, string value) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));
    private static string HmacSha1Hex(byte[] key, string value) => Convert.ToHexString(HMACSHA1.HashData(key, Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Sha256Hex(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Sha1Hex(string value) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

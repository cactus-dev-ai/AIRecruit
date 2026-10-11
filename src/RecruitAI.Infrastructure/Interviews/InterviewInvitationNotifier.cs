using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RecruitAI.Infrastructure.Interviews;

public sealed record TencentCloudSesSettings(
    bool Enabled, string Endpoint, string Region, string SecretId, string SecretKey,
    string FromEmailAddress, string Subject, long TemplateId, string TemplateCodeKey);

public sealed record AliyunSmsSettings(
    bool Enabled, string Endpoint, string AccessKeyId, string AccessKeySecret,
    string SignName, string TemplateCode, string TemplateJobKey, string TemplateInviteCodeKey);

public sealed record InvitationNotification(
    long InvitationId, string Channel, string CandidateName, string JobName, string Destination,
    string H5Url, string Code, DateTime ExpireTime);

public sealed record InvitationNotificationResult(
    string Channel, bool Sent, string MaskedDestination, string Provider, string? ProviderMessageId,
    string? ErrorCode, string? ErrorMessage);

public sealed record AliyunSmsDiagnostics(
    bool Enabled, string Endpoint, string AccessKeyIdFingerprint, bool AccessKeySecretConfigured,
    string SignName, int SignNameLength, string SignNameUtf8Hex, string TemplateCode,
    string TemplateJobKey, string TemplateInviteCodeKey);

public sealed class InterviewInvitationNotifier(
    HttpClient http,
    TencentCloudSesSettings emailSettings,
    AliyunSmsSettings smsSettings)
{
    public AliyunSmsDiagnostics GetSmsDiagnostics() => new(
        smsSettings.Enabled,
        smsSettings.Endpoint,
        Fingerprint(smsSettings.AccessKeyId),
        !string.IsNullOrWhiteSpace(smsSettings.AccessKeySecret),
        smsSettings.SignName,
        smsSettings.SignName.Length,
        Convert.ToHexString(Encoding.UTF8.GetBytes(smsSettings.SignName)),
        smsSettings.TemplateCode,
        smsSettings.TemplateJobKey,
        smsSettings.TemplateInviteCodeKey);

    public Task<InvitationNotificationResult> SendAsync(InvitationNotification request, CancellationToken ct) =>
        request.Channel switch
        {
            "EMAIL" => SendEmailAsync(request, ct),
            "SMS" => SendSmsAsync(request, ct),
            _ => Task.FromResult(Failed(request.Channel, "", "UNSUPPORTED_CHANNEL", "不支持的通知渠道。"))
        };

    private async Task<InvitationNotificationResult> SendEmailAsync(InvitationNotification request, CancellationToken ct)
    {
        var masked = MaskEmail(request.Destination);
        if (string.IsNullOrWhiteSpace(request.Destination)) return Failed("EMAIL", masked, "EMAIL_MISSING", "候选人未填写邮箱。", "TENCENT_SES");
        if (!emailSettings.Enabled || string.IsNullOrWhiteSpace(emailSettings.Endpoint) || string.IsNullOrWhiteSpace(emailSettings.SecretId) ||
            string.IsNullOrWhiteSpace(emailSettings.SecretKey) || string.IsNullOrWhiteSpace(emailSettings.FromEmailAddress) || emailSettings.TemplateId <= 0)
            return Failed("EMAIL", masked, "EMAIL_NOT_CONFIGURED", "腾讯云 SES 密钥、发件地址或模板尚未配置。", "TENCENT_SES");
        try
        {
            var configuredCodeKey = string.IsNullOrWhiteSpace(emailSettings.TemplateCodeKey) ? "CODE" : emailSettings.TemplateCodeKey.Trim();
            var templateValues = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [configuredCodeKey] = request.Code
            };
            // Tencent SES template variables are case-sensitive. Keep both aliases so an existing
            // production config using "code" can safely render a template authored as {{CODE}}.
            templateValues["CODE"] = request.Code;
            templateValues["code"] = request.Code;
            var templateData = JsonSerializer.Serialize(templateValues);
            var body = new
            {
                emailSettings.FromEmailAddress,
                Subject = string.IsNullOrWhiteSpace(emailSettings.Subject) ? "面试邀请" : emailSettings.Subject,
                Destination = new[] { request.Destination },
                Template = new { TemplateID = emailSettings.TemplateId, TemplateData = templateData },
                TriggerType = 1
            };
            using var response = await SendTencentApiAsync(body, "ses", emailSettings.Endpoint, "SendEmail", "2020-10-02", emailSettings.Region, emailSettings.SecretId, emailSettings.SecretKey, ct);
            var root = response.RootElement.GetProperty("Response");
            if (root.TryGetProperty("Error", out var apiError))
                return Failed("EMAIL", masked, GetString(apiError, "Code") ?? "SES_API_FAILED", SafeError(GetString(apiError, "Message")), "TENCENT_SES");
            return new("EMAIL", true, masked, "TENCENT_SES", GetString(root, "MessageId") ?? GetString(root, "RequestId"), null, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failed("EMAIL", masked, "EMAIL_SEND_FAILED", SafeError(exception.Message), "TENCENT_SES");
        }
    }

    private async Task<InvitationNotificationResult> SendSmsAsync(InvitationNotification request, CancellationToken ct)
    {
        var phone = NormalizePhone(request.Destination);
        var masked = MaskPhone(phone);
        if (phone.Length != 11) return Failed("SMS", masked, "PHONE_MISSING", "候选人未填写有效手机号。", "ALIYUN_SMS");
        if (!smsSettings.Enabled || string.IsNullOrWhiteSpace(smsSettings.Endpoint) || string.IsNullOrWhiteSpace(smsSettings.AccessKeyId) ||
            string.IsNullOrWhiteSpace(smsSettings.AccessKeySecret) || string.IsNullOrWhiteSpace(smsSettings.SignName) || string.IsNullOrWhiteSpace(smsSettings.TemplateCode))
            return Failed("SMS", masked, "SMS_NOT_CONFIGURED", WithSmsDiagnostics("阿里云短信 AccessKey、签名或模板尚未配置。"), "ALIYUN_SMS");
        try
        {
            var templateParam = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                [string.IsNullOrWhiteSpace(smsSettings.TemplateJobKey) ? "job" : smsSettings.TemplateJobKey] = request.JobName,
                [string.IsNullOrWhiteSpace(smsSettings.TemplateInviteCodeKey) ? "inviteCode" : smsSettings.TemplateInviteCodeKey] = request.Code
            });
            var parameters = new Dictionary<string, string>
            {
                ["PhoneNumbers"] = phone,
                ["SignName"] = smsSettings.SignName,
                ["TemplateCode"] = smsSettings.TemplateCode,
                ["TemplateParam"] = templateParam
            };
            using var response = await SendAliyunSmsAsync(parameters, ct);
            var root = response.RootElement;
            var code = GetString(root, "Code") ?? "UNKNOWN";
            return code.Equals("OK", StringComparison.OrdinalIgnoreCase)
                ? new("SMS", true, masked, "ALIYUN_SMS", GetString(root, "BizId") ?? GetString(root, "RequestId"), null, null)
                : Failed("SMS", masked, code, WithSmsDiagnostics(SafeError(GetString(root, "Message"))), "ALIYUN_SMS");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failed("SMS", masked, "SMS_SEND_FAILED", WithSmsDiagnostics(SafeError(exception.Message)), "ALIYUN_SMS");
        }
    }

    private async Task<JsonDocument> SendTencentApiAsync(object body, string service, string host, string action, string version, string region, string secretId, string secretKey, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = null });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var date = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var canonicalHeaders = $"content-type:application/json; charset=utf-8\nhost:{host}\nx-tc-action:{action.ToLowerInvariant()}\n";
        const string signedHeaders = "content-type;host;x-tc-action";
        var canonicalRequest = $"POST\n/\n\n{canonicalHeaders}\n{signedHeaders}\n{Sha256Hex(payload)}";
        var scope = $"{date}/{service}/tc3_request";
        var stringToSign = $"TC3-HMAC-SHA256\n{timestamp}\n{scope}\n{Sha256Hex(canonicalRequest)}";
        var secretDate = HmacSha256(Encoding.UTF8.GetBytes("TC3" + secretKey), date);
        var secretService = HmacSha256(secretDate, service);
        var secretSigning = HmacSha256(secretService, "tc3_request");
        var signature = Convert.ToHexString(HmacSha256(secretSigning, stringToSign)).ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/");
        request.Headers.TryAddWithoutValidation("Authorization", $"TC3-HMAC-SHA256 Credential={secretId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
        request.Headers.TryAddWithoutValidation("Host", host);
        request.Headers.TryAddWithoutValidation("X-TC-Action", action);
        request.Headers.TryAddWithoutValidation("X-TC-Version", version);
        request.Headers.TryAddWithoutValidation("X-TC-Timestamp", timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-TC-Region", region);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var result = await http.SendAsync(request, ct);
        var content = await result.Content.ReadAsStringAsync(ct);
        result.EnsureSuccessStatusCode();
        return JsonDocument.Parse(content);
    }

    private async Task<JsonDocument> SendAliyunSmsAsync(IReadOnlyDictionary<string, string> parameters, CancellationToken ct)
    {
        const string action = "SendSms", version = "2017-05-25";
        var host = smsSettings.Endpoint.Trim();
        var query = string.Join("&", parameters.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{Encode(x.Key)}={Encode(x.Value)}"));
        var payloadHash = Sha256Hex(string.Empty);
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        const string signedHeaders = "host;x-acs-action;x-acs-content-sha256;x-acs-date;x-acs-signature-nonce;x-acs-version";
        var canonicalHeaders = $"host:{host}\nx-acs-action:{action}\nx-acs-content-sha256:{payloadHash}\nx-acs-date:{date}\nx-acs-signature-nonce:{nonce}\nx-acs-version:{version}\n";
        var canonicalRequest = $"POST\n/\n{query}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
        var stringToSign = $"ACS3-HMAC-SHA256\n{Sha256Hex(canonicalRequest)}";
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(smsSettings.AccessKeySecret), Encoding.UTF8.GetBytes(stringToSign))).ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/?{query}");
        request.Headers.TryAddWithoutValidation("Host", host);
        request.Headers.TryAddWithoutValidation("x-acs-action", action);
        request.Headers.TryAddWithoutValidation("x-acs-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("x-acs-date", date);
        request.Headers.TryAddWithoutValidation("x-acs-signature-nonce", nonce);
        request.Headers.TryAddWithoutValidation("x-acs-version", version);
        request.Headers.TryAddWithoutValidation("Authorization", $"ACS3-HMAC-SHA256 Credential={smsSettings.AccessKeyId},SignedHeaders={signedHeaders},Signature={signature}");
        request.Content = new ByteArrayContent([]);
        using var result = await http.SendAsync(request, ct);
        var content = await result.Content.ReadAsStringAsync(ct);
        result.EnsureSuccessStatusCode();
        return JsonDocument.Parse(content);
    }

    private static InvitationNotificationResult Failed(string channel, string masked, string code, string? message, string provider = "LOCAL") =>
        new(channel, false, masked, provider, null, code, message);
    private static string NormalizePhone(string? value) { var digits = new string((value ?? "").Where(char.IsDigit).ToArray()); return digits.StartsWith("86") && digits.Length == 13 ? digits[2..] : digits; }
    private static string MaskPhone(string phone) => phone.Length == 11 ? phone[..3] + "****" + phone[^4..] : "未提供";
    private static string MaskEmail(string? value) { if (string.IsNullOrWhiteSpace(value) || !value.Contains('@')) return "未提供"; var parts=value.Split('@',2); if(parts[0].Length==0||parts[1].Length==0)return "未提供"; return (parts[0].Length<=2?parts[0][..1]+"*":parts[0][..2]+"***")+"@"+parts[1]; }
    private static string? GetString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? SafeError(string? value) => string.IsNullOrWhiteSpace(value) ? "服务未返回错误说明。" : value[..Math.Min(value.Length, 500)];
    private string WithSmsDiagnostics(string? message)
    {
        var d=GetSmsDiagnostics();
        return $"{message} [安全诊断 ak={d.AccessKeyIdFingerprint}; sign={d.SignName}; signLen={d.SignNameLength}; signUtf8={d.SignNameUtf8Hex}; template={d.TemplateCode}; endpoint={d.Endpoint}]";
    }
    private static string Fingerprint(string value) => string.IsNullOrWhiteSpace(value) ? "EMPTY" : Sha256Hex(value)[..12].ToUpperInvariant();
    private static byte[] HmacSha256(byte[] key, string value) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));
    private static string Sha256Hex(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Encode(string value) => Uri.EscapeDataString(value).Replace("%7E", "~", StringComparison.OrdinalIgnoreCase);
}

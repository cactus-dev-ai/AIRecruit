using System.Text;
using System.Text.Json;
using RecruitAI.Application.Common;
using RecruitAI.Application.Knowledge;
using RecruitAI.Infrastructure.Tasks;

namespace RecruitAI.Web.Infrastructure;

public sealed class KnowledgeAiDraftService(AgentWorkerControlSettings settings)
{
    public async Task<KnowledgeAiDraftResponse> GenerateAsync(KnowledgeAiDraftRequest request, CancellationToken ct)
    {
        if (request.Title.Trim().Length is < 1 or > 300) throw new BusinessException("KNOWLEDGE_TITLE_INVALID", "请先填写知识标题。");
        // A weak first draft is automatically rewritten once by the Agent, so allow both AI calls to finish.
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(260) };
        var body = JsonSerializer.Serialize(new
        {
            title = request.Title,
            document_type = request.DocumentType,
            content = request.Content,
            instruction = request.Instruction
        });
        try
        {
            using var response = await client.PostAsync(settings.BaseUrl.TrimEnd('/') + "/knowledge/draft", new StringContent(body, Encoding.UTF8, "application/json"), ct);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(ct);
                var detail = ReadErrorDetail(responseBody);
                throw new BusinessException("KNOWLEDGE_AI_FAILED", $"AI生成知识草稿失败：{detail ?? $"HTTP {(int)response.StatusCode}"}", 503);
            }
            var result = JsonSerializer.Deserialize<KnowledgeAiDraftResponse>(await response.Content.ReadAsStringAsync(ct), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return result is null || string.IsNullOrWhiteSpace(result.Content) ? throw new BusinessException("KNOWLEDGE_AI_INVALID_RESPONSE", "AI没有返回有效知识正文。", 503) : result;
        }
        catch (HttpRequestException) { throw new BusinessException("AGENT_OFFLINE", "Python Agent服务未启动，无法生成知识草稿。", 503); }
    }

    private static string? ReadErrorDetail(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("detail", out var detail)) return null;
            if (detail.ValueKind == JsonValueKind.String) return detail.GetString();
            if (detail.ValueKind == JsonValueKind.Array && detail.GetArrayLength() > 0)
            {
                var first = detail[0];
                var field = first.TryGetProperty("loc", out var location)
                    ? string.Join(".", location.EnumerateArray().Select(x => x.ToString()).Skip(1)) : "请求字段";
                var message = first.TryGetProperty("msg", out var error) ? error.GetString() : "格式不正确";
                return $"{field} {message}";
            }
        }
        catch (JsonException) { }
        return null;
    }
}

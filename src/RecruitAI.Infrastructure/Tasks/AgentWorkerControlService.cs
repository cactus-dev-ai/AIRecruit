using System.Diagnostics;
using System.Net.Http.Json;
using RecruitAI.Application.Common;

namespace RecruitAI.Infrastructure.Tasks;

public sealed record AgentWorkerControlSettings(string BaseUrl, string PythonPath, string WorkingDirectory);
public sealed record AgentWorkerStatus(bool ServiceOnline, bool Running);

public sealed class AgentWorkerControlService(AgentWorkerControlSettings settings)
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<AgentWorkerStatus> GetStatusAsync(CancellationToken ct)
    {
        try
        {
            var status = await _client.GetFromJsonAsync<WorkerResponse>(BuildUrl("/worker/status"), ct);
            return new(true, status?.Running == true);
        }
        catch (HttpRequestException) { return new(false, false); }
        catch (TaskCanceledException) { return new(false, false); }
    }

    public async Task<AgentWorkerStatus> StartAsync(CancellationToken ct)
    {
        var current = await GetStatusAsync(ct);
        if (!current.ServiceOnline)
        {
            if (!File.Exists(settings.PythonPath) || !Directory.Exists(settings.WorkingDirectory))
                throw new BusinessException("AGENT_RUNTIME_NOT_FOUND", "未找到 Python Agent 运行环境。", 503);
            Process.Start(new ProcessStartInfo
            {
                FileName = settings.PythonPath,
                Arguments = "main.py",
                WorkingDirectory = settings.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            for (var retry = 0; retry < 20; retry++)
            {
                await Task.Delay(300, ct);
                current = await GetStatusAsync(ct);
                if (current.ServiceOnline) break;
            }
        }
        if (!current.ServiceOnline) throw new BusinessException("AGENT_SERVICE_UNAVAILABLE", "Python Agent 服务未能启动。", 503);
        using var response = await _client.PostAsync(BuildUrl("/worker/start"), null, ct);
        response.EnsureSuccessStatusCode();
        return new(true, true);
    }

    public async Task<AgentWorkerStatus> PauseAsync(CancellationToken ct)
    {
        var current = await GetStatusAsync(ct);
        if (!current.ServiceOnline) return current;
        using var response = await _client.PostAsync(BuildUrl("/worker/pause"), null, ct);
        response.EnsureSuccessStatusCode();
        return new(true, false);
    }

    private string BuildUrl(string path) => settings.BaseUrl.TrimEnd('/') + path;
    private sealed record WorkerResponse(bool Running);
}

using System.Text;
using System.Text.Json;

namespace Job51.WinForms;

internal sealed class DiagnosticLogService
{
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public string LogDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Logs");

    public string CurrentLogPath => Path.Combine(LogDirectory, $"diagnostic-{DateTime.Now:yyyyMMdd}.jsonl");

    public void Write(string eventName, object? data = null)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var entry = new
            {
                timestamp = DateTimeOffset.Now,
                event_name = eventName,
                data
            };
            var line = JsonSerializer.Serialize(entry, _options) + Environment.NewLine;
            lock (_sync)
                File.AppendAllText(CurrentLogPath, line, new UTF8Encoding(false));
        }
        catch
        {
            // Diagnostics must never interrupt resume processing.
        }
    }
}

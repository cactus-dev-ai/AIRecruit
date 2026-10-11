using System.Text.Json;
using System.Text.RegularExpressions;

namespace RecruitAI.Infrastructure.Imports;

public static class ResumeJsonValidator
{
    private static readonly Regex SourcePattern = new("^[A-Za-z0-9-]{1,100}$", RegexOptions.CultureInvariant);
    public const int MaxCandidateNameCharacters = 100;
    public const int MaxTalentIdCharacters = 100;
    public const int MaxSourceRouteCharacters = 200;
    public const int MaxHeadlineCharacters = 200;
    public const int MaxVisibleTextCharacters = 500_000;

    public static bool IsValid(string path, long maxFileBytes, string? originalFileName = null)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > maxFileBytes) return false;
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!TryRequiredText(document.RootElement, "talent_id", MaxTalentIdCharacters, out var talentId)) return false;
            if (!FileNameMatches(originalFileName ?? Path.GetFileName(path), talentId)) return false;
            if (!TryRequiredText(document.RootElement, "source_route", MaxSourceRouteCharacters, out _)) return false;
            if (!TryRequiredText(document.RootElement, "candidate_name", MaxCandidateNameCharacters, out _)) return false;
            if (!TryRequiredText(document.RootElement, "visible_text", MaxVisibleTextCharacters, out _)) return false;
            if (document.RootElement.TryGetProperty("headline", out var headline) &&
                (headline.ValueKind != JsonValueKind.String || (headline.GetString()?.Length ?? 0) > MaxHeadlineCharacters)) return false;
            return true;
        }
        catch (JsonException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool FileNameMatches(string fileName, string talentId)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var separator = stem.IndexOf('_');
        if (separator <= 0 || separator == stem.Length - 1) return false;
        var source = stem[..separator];
        var fileTalentId = stem[(separator + 1)..];
        return SourcePattern.IsMatch(source) &&
               (string.Equals(fileTalentId, talentId, StringComparison.OrdinalIgnoreCase) ||
                fileTalentId.StartsWith(talentId + "_", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryRequiredText(JsonElement root, string name, int maxCharacters, out string value)
    {
        value = "";
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString()?.Trim() ?? "";
        return value.Length is > 0 && value.Length <= maxCharacters;
    }
}

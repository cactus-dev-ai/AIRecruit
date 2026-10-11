using System.Text.RegularExpressions;

namespace Job51.WinForms;

internal static partial class ResumeTextCleaner
{
    private static readonly HashSet<string> UiLines = new(StringComparer.OrdinalIgnoreCase)
    {
        "通知", "手机版", "帮助", "企业管理", "扫码分享", "在线简历", "Resume", "举报",
        "电话沟通", "附件个人信息", "人才罗盘", "立即Hi聊", "不感兴趣"
    };

    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => WhitespaceRegex().Replace(line.Replace('\u00A0', ' '), " ").Trim())
            .Where(line => line.Length > 0)
            .ToList();

        var footer = lines.FindIndex(line =>
            line.Contains("声明：以上人才信息仅供", StringComparison.Ordinal) ||
            string.Equals(line, "操作动态", StringComparison.Ordinal));
        if (footer >= 0) lines = lines.Take(footer).ToList();

        var result = new List<string>(lines.Count);
        var beforeResumeContent = true;
        foreach (var line in lines)
        {
            if (line is "求职意向" or "个人优势" or "工作经历" or "项目经验" or "教育经历")
                beforeResumeContent = false;
            if (UiLines.Contains(line) || TalentIdHeaderRegex().IsMatch(line) || UnlockAnalysisRegex().IsMatch(line) ||
                line.StartsWith("该人才偏好电话，推荐您", StringComparison.Ordinal) ||
                line.StartsWith("您可对候选人的在职状态进行相关询问", StringComparison.Ordinal) ||
                line.StartsWith("候选人似乎处于离职状态", StringComparison.Ordinal) ||
                (beforeResumeContent && NotificationBadgeRegex().IsMatch(line)) || IsRepeatedWatermark(line))
                continue;
            result.Add(line);
        }
        return string.Join('\n', result).Trim();
    }

    public static (string CandidateName, string VisibleText) CleanCandidate(string? candidateName, string? text)
    {
        var cleaned = Clean(text);
        var lines = cleaned.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var normalizedName = candidateName?.Trim() ?? string.Empty;
        if (lines.Count >= 2 && !MaskedPersonNameRegex().IsMatch(lines[0]) && MaskedPersonNameRegex().IsMatch(lines[1]))
        {
            normalizedName = lines[1];
            lines.RemoveAt(0);
            cleaned = string.Join('\n', lines);
        }
        return (normalizedName, cleaned);
    }

    private static bool IsRepeatedWatermark(string line)
    {
        var parts = WhitespaceRegex().Split(line).Where(part => part.Length > 0).ToArray();
        if (parts.Length >= 3 && parts[0].Length >= 6 && parts.All(part => part == parts[0])) return true;
        var compact = string.Concat(parts);
        for (var length = 6; length <= compact.Length / 3; length++)
        {
            if (compact.Length % length != 0 || compact.Length / length < 3) continue;
            var unit = compact[..length];
            if (string.Concat(Enumerable.Repeat(unit, compact.Length / length)) == compact) return true;
        }
        return false;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^人才ID\s*[:：].*活跃时间\s*[:：]", RegexOptions.CultureInvariant)]
    private static partial Regex TalentIdHeaderRegex();

    [GeneratedRegex(@"^解锁\d+项人才行为分析", RegexOptions.CultureInvariant)]
    private static partial Regex UnlockAnalysisRegex();

    [GeneratedRegex(@"^\d{1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex NotificationBadgeRegex();

    [GeneratedRegex(@"^[\u3400-\u9FFF·]{1,8}(先生|女士)$", RegexOptions.CultureInvariant)]
    private static partial Regex MaskedPersonNameRegex();
}

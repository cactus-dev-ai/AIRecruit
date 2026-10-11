using System.Text.Json;
using System.Text.Json.Serialization;

namespace Job51.WinForms;

internal sealed class AppSettings
{
    public string LoginUrl { get; set; } = "https://ehire.51job.com/";
    public string TalentSearchUrl { get; set; } = "https://ehire.51job.com/Revision/talent/search?rt=1786244528973";
    public string TalentManagementUrl { get; set; } = "https://ehire.51job.com/Revision/talent/management";
    public AuthenticationSettings Authentication { get; set; } = new();
    public DownloadSettings Download { get; set; } = new();
    public ServerUploadSettings ServerUpload { get; set; } = new();
    public SearchSettings Search { get; set; } = new();
    public NetworkCaptureSettings NetworkCapture { get; set; } = new();
}

internal sealed class NetworkCaptureSettings
{
    public bool Enabled { get; set; } = true;
    public bool ClearAfterSuccessfulUpload { get; set; } = true;
    public int MaximumBodyBytes { get; set; } = 4 * 1024 * 1024;
    public string[] AllowedHosts { get; set; } = ["51job.com"];
    public string[] UrlKeywords { get; set; } =
        ["talent", "resume", "search", "candidate", "recommend", "job"];
}

internal sealed class ServerUploadSettings
{
    public string ApiUrl { get; set; } = "https://hr.siekon.com/api/third-party-resumes";
    public string ApiKey { get; set; } = "51job-local-import-2026";
    public int TimeoutSeconds { get; set; } = 60;
    public int MaximumFilesPerRun { get; set; } = 100;
}

internal sealed class AuthenticationSettings
{
    public string CookieUrl { get; set; } = "https://ehire.51job.com/";
    public string[] RequiredCookieNames { get; set; } = [];
    public string[] CookieNameHints { get; set; } = ["auth", "token", "session", "sso", "ticket", "login", "user"];
}

internal sealed class DownloadSettings
{
    public int MinimumPerRun { get; set; } = 50;
    public int MaximumPerRun { get; set; } = 100;
    public int DefaultPerRun { get; set; } = 100;
    public int MaxPagesPerRun { get; set; } = 20;
    public int DetailOpenDelayMilliseconds { get; set; } = 650;
    public int ListRestoreDelayMilliseconds { get; set; } = 500;
    public int PageChangeDelayMilliseconds { get; set; } = 650;
    public int LoadMoreBatchSize { get; set; } = 50;
    public int LoadMoreDelayMilliseconds { get; set; } = 1500;
    public bool DiscardFailures { get; set; } = true;
}

internal sealed class SearchSettings
{
    public bool ClearExistingFilters { get; set; } = true;
    public bool ExcludeViewedCandidates { get; set; } = true;
    public int InteractionDelayMilliseconds { get; set; } = 1000;
    public List<SearchFilterSetting> Filters { get; set; } = [];
}

internal sealed class SearchFilterSetting
{
    public string ControlName { get; set; } = string.Empty;
    public string[] ControlAliases { get; set; } = [];
    public string[] Options { get; set; } = [];
    public bool UseDialogSearch { get; set; }
    public bool OpenMoreFilters { get; set; }
    public string? ConfirmButtonText { get; set; } = "确定";
}

internal sealed class SearchExecution
{
    [JsonPropertyName("external_job_id")]
    public string ExternalJobId { get; init; } = string.Empty;

    [JsonPropertyName("selected_position")]
    public string SelectedPosition { get; init; } = string.Empty;

    [JsonPropertyName("keyword_expression")]
    public string KeywordExpression { get; init; } = string.Empty;

    [JsonPropertyName("configured_filters")]
    public List<SearchFilterSetting> ConfiguredFilters { get; init; } = [];
}

internal sealed class SourceJobData
{
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = "51JOB";

    [JsonPropertyName("external_job_id")]
    public string ExternalJobId { get; init; } = string.Empty;

    [JsonPropertyName("external_job_name")]
    public string ExternalJobName { get; init; } = string.Empty;

    [JsonPropertyName("download_batch")]
    public string DownloadBatch { get; init; } = string.Empty;
}

internal sealed class SearchPageContext
{
    [JsonPropertyName("selected_position")]
    public string SelectedPosition { get; set; } = string.Empty;

    [JsonPropertyName("keyword_expression")]
    public string KeywordExpression { get; set; } = string.Empty;

    [JsonPropertyName("detection_source")]
    public string DetectionSource { get; set; } = string.Empty;
}

internal sealed class CandidateTarget
{
    [JsonPropertyName("talent_id")]
    public string TalentId { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("dom_index")]
    public int DomIndex { get; set; } = -1;
}

internal sealed class CandidatePageData
{
    [JsonPropertyName("talent_id")]
    public string TalentId { get; set; } = string.Empty;

    [JsonPropertyName("source_route")]
    public string SourceRoute { get; set; } = string.Empty;

    [JsonPropertyName("page_title")]
    public string PageTitle { get; set; } = string.Empty;

    [JsonPropertyName("candidate_name")]
    public string? CandidateName { get; set; }

    [JsonPropertyName("headline")]
    public string? Headline { get; set; }

    [JsonPropertyName("visible_text")]
    public string VisibleText { get; set; } = string.Empty;

    [JsonPropertyName("attachment_links")]
    public string[] AttachmentLinks { get; set; } = [];

    [JsonPropertyName("source_job")]
    public SourceJobData? SourceJob { get; set; }

    [JsonPropertyName("downloaded_at")]
    public DateTimeOffset DownloadedAt { get; set; }
}

internal sealed class ManagementApiResumeData
{
    [JsonPropertyName("talent_id")]
    public string TalentId { get; init; } = string.Empty;

    [JsonPropertyName("source_route")]
    public string SourceRoute { get; init; } = "/resume/administer/talent_pool_resume";

    [JsonPropertyName("candidate_name")]
    public string? CandidateName { get; init; }

    [JsonPropertyName("is_new")]
    public bool IsNew { get; init; } = true;

    [JsonPropertyName("downloaded_at")]
    public DateTimeOffset DownloadedAt { get; init; } = DateTimeOffset.Now;

    [JsonPropertyName("api_payload")]
    public JsonElement ApiPayload { get; init; }
}

internal sealed record ManagementApiSnapshot(
    DateTimeOffset CapturedAt,
    int CurrentPageCount,
    IReadOnlyList<ManagementApiResumeData> NewResumes);

internal sealed class TalentJobApiData
{
    public string JobId { get; init; } = string.Empty;
    public string JobName { get; init; } = string.Empty;
    public string FunctionTypeCode { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string PublishStatus { get; init; } = string.Empty;
    public string AiKeyword { get; init; } = string.Empty;
}

internal sealed record TalentJobApiSnapshot(
    DateTimeOffset CapturedAt,
    string SourceRoute,
    IReadOnlyList<TalentJobApiData> Jobs);

internal sealed record TalentSearchApiCandidate(
    string TalentId,
    string CandidateName);

internal sealed record TalentSearchApiSnapshot(
    DateTimeOffset CapturedAt,
    string Route,
    int CurrentPageCount,
    int TotalCount,
    string RequestId,
    string JobId,
    IReadOnlyList<TalentSearchApiCandidate> Candidates);

internal sealed record TalentSearchProgress(
    string ResumeJobId,
    string ResumeJobName,
    DateTimeOffset UpdatedAt);

internal sealed class DownloadRecord
{
    public required string TalentId { get; init; }
    public required string SourceRoute { get; init; }
    public string? CandidateName { get; init; }
    public required string JsonPath { get; init; }
    public required string BatchType { get; init; }
    public string? SourceJobId { get; init; }
    public string? SourceJobName { get; init; }
    public DateTimeOffset DownloadedAt { get; init; } = DateTimeOffset.Now;
}

internal sealed class DiscardRecord
{
    public required string TalentId { get; init; }
    public required string Reason { get; init; }
    public string? PositionName { get; init; }
    public DateTimeOffset DiscardedAt { get; init; } = DateTimeOffset.Now;
}

internal sealed class BatchIndex
{
    [JsonPropertyName("batch_type")]
    public string BatchType { get; init; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    [JsonPropertyName("search")]
    public SearchExecution? Search { get; init; }

    [JsonPropertyName("source_route")]
    public string SourceRoute { get; init; } = string.Empty;

    [JsonPropertyName("database_path")]
    public string DatabasePath { get; init; } = string.Empty;

    [JsonPropertyName("discovered_count")]
    public int DiscoveredCount { get; init; }

    [JsonPropertyName("downloaded_count")]
    public int DownloadedCount { get; init; }

    [JsonPropertyName("skipped_duplicate_count")]
    public int SkippedDuplicateCount { get; init; }

    [JsonPropertyName("discarded_count")]
    public int DiscardedCount { get; init; }

    [JsonPropertyName("files")]
    public List<string> Files { get; init; } = [];

    [JsonPropertyName("notes")]
    public List<string> Notes { get; init; } = [];
}

internal sealed record BatchDownloadResult(
    int DownloadedCount,
    int SkippedDuplicateCount,
    int DiscardedCount,
    bool CanContinueImmediately,
    string Folder);

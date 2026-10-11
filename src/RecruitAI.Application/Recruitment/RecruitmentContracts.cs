namespace RecruitAI.Application.Recruitment;

public sealed record JobDto(
    long Id, string Name, string? Description, string Requirement, string? SkillRequirement,
    int? MinExperienceMonths, string? EducationRequirement, string Status,
    decimal SkillWeight, decimal ProjectWeight, decimal ExperienceWeight,
    decimal EducationWeight, int ScoreConfigVersion, string RowVersion, DateTime UpdateTime,
    int ResumeCount, decimal DutyWeight, decimal ProductWeight, decimal IndustryWeight,
    decimal TenureWeight, decimal CompanyEnvironmentWeight, int? MaxCandidateAge,
    decimal? MaxExpectedMonthlySalary);

public sealed record SaveJobRequest(
    string Name, string? Description, string Requirement, string? SkillRequirement,
    int? MinExperienceMonths, string? EducationRequirement, string? RowVersion);

public sealed record UpdateScoreConfigRequest(
    decimal SkillWeight, decimal ProjectWeight, decimal ExperienceWeight,
    decimal EducationWeight, decimal DutyWeight, decimal ProductWeight,
    decimal IndustryWeight, decimal TenureWeight, decimal CompanyEnvironmentWeight,
    string? RuleJson, string RowVersion);

public sealed record UpdateJobCleanupRulesRequest(
    int? MaxCandidateAge, decimal? MaxExpectedMonthlySalary, string RowVersion);

public sealed record JobCleanupResult(int ScannedCount, int CleanedCount, int UnknownCount);
public sealed record JobCleanupRestoreResult(int RestoredCount, int LegacyRestoredCount);

public sealed record JobRankingItemDto(long CandidateJobId,long CandidateId,string CandidateName,decimal? AiScore,
    decimal? SkillScore,decimal? ProjectScore,decimal? ExperienceScore,decimal? EducationScore,
    string? CurrentCompany,string? Recommendation,int? ScoreConfigVersion,DateTime UpdateTime,bool IsFavorite,
    DateTime ResumeImportTime);
public sealed record ScoreRunDto(long Id,int RunNo,int ScoreConfigVersion,
    decimal SkillWeight,decimal ProjectWeight,decimal ExperienceWeight,decimal EducationWeight,
    decimal DutyWeight,decimal ProductWeight,decimal IndustryWeight,decimal TenureWeight,decimal CompanyEnvironmentWeight,
    int WaitingCount,int RunningCount,int SuccessCount,int FailedCount,int CancelledCount,DateTime CreateTime);
public sealed record QueueScoreRunResult(long RunId,int RunNo,int Queued);
public sealed record JobRankingPage(IReadOnlyList<JobRankingItemDto> Items,int TotalCount,int Page,int PageSize,
    int WaitingCount,int RunningCount,int FailedCount,ScoreRunDto? Run);

public sealed record MergeJobRequest(long TargetJobId, string? Reason);
public sealed record JobMergeTargetDto(long Id,string Name,string Status,int ResumeCount,bool Eligible,string? DisabledReason);
public sealed record FavoriteCandidateJobDto(long CandidateJobId,long CandidateId,string CandidateName,long JobId,string JobName,int Rank,
    decimal? AiScore,decimal? SkillScore,decimal? ProjectScore,decimal? ExperienceScore,decimal? EducationScore,
    string? CurrentCompany,string? Recommendation,int? ScoreConfigVersion,DateTime FavoriteTime,
    bool HasPhone,bool HasActiveInterview,long? ActiveQuestionSetId);
public sealed record FavoriteCandidateJobPage(IReadOnlyList<FavoriteCandidateJobDto> Items,int TotalCount,int Page,int PageSize);

public sealed record JobProfileDto(
    long JobId, long KnowledgeDocumentId, string Title, string Content,
    string Status, int Version, string RowVersion, DateTime UpdateTime);

public sealed record SaveJobProfileRequest(string Title, string Content, string RowVersion);

public sealed record CandidateListItemDto(
    long Id, string Name, string? Phone, string? Email, string? Source, string Status,
    string? HighestEducation, int? TotalExperienceMonths, long? CurrentResumeId,
    DateTime UpdateTime, string RowVersion, string? MatchedJobs);

public sealed record CandidatePage(IReadOnlyList<CandidateListItemDto> Items, int TotalCount, int Page, int PageSize);

public sealed record ResumeVersionDto(
    long Id, string FileName, string FileExtension, long FileSize, string ParseStatus,
    bool IsCurrent, bool IsOptimized, DateTime CreateTime);

public sealed record CandidateEducationDto(string SchoolName, string? Major, string? Degree, DateTime? StartDate, DateTime? EndDate, string? Description);
public sealed record CandidateWorkExperienceDto(string CompanyName, string? JobTitle, DateTime? StartDate, DateTime? EndDate, string? Description, bool IsCurrent);
public sealed record CandidateProjectDto(string ProjectName, string? RoleName, string? Technology, DateTime? StartDate, DateTime? EndDate, string? Description);

public sealed record CandidateDetailDto(
    CandidateListItemDto Candidate, string? Summary, string? CurrentCompany, string? CurrentTitle,
    string? TalentId, string? ResumeSource, string? RawText,
    IReadOnlyList<string> Skills, IReadOnlyList<CandidateEducationDto> Educations,
    IReadOnlyList<CandidateWorkExperienceDto> WorkExperiences, IReadOnlyList<CandidateProjectDto> Projects,
    IReadOnlyList<ResumeVersionDto> Resumes);

public sealed record UpdateCandidateRequest(string Name, string? Phone, string? Email, string RowVersion);
public sealed record ResumeDownloadInfo(string FileName, string FileExtension, string RelativePath);

public interface IRecruitmentService
{
    Task<IReadOnlyList<JobDto>> ListJobsAsync(int page, int pageSize, string? status, string? keyword, CancellationToken ct);
    Task<JobDto?> GetJobAsync(long id, CancellationToken ct);
    Task<long> CreateJobAsync(SaveJobRequest request, long userId, CancellationToken ct);
    Task UpdateJobAsync(long id, SaveJobRequest request, CancellationToken ct);
    Task ChangeJobStatusAsync(long id, string action, CancellationToken ct);
    Task DeleteJobAsync(long id, CancellationToken ct);
    Task UpdateJobCleanupRulesAsync(long id, UpdateJobCleanupRulesRequest request, CancellationToken ct);
    Task<JobCleanupResult> CleanupJobCandidatesAsync(long id, string criterion, long userId, CancellationToken ct);
    Task<JobCleanupRestoreResult> RestoreJobCandidatesAsync(long id, long userId, CancellationToken ct);
    Task UpdateScoreConfigAsync(long id, UpdateScoreConfigRequest request, long userId, CancellationToken ct);
    Task<QueueScoreRunResult> QueueJobScoringAsync(long id,long userId,CancellationToken ct);
    Task<IReadOnlyList<ScoreRunDto>> ListScoreRunsAsync(long id,CancellationToken ct);
    Task<JobRankingPage> GetJobRankingAsync(long id,long? runId,int page,int pageSize,DateOnly? resumeFrom,DateOnly? resumeTo,string? talentId,CancellationToken ct);
    Task<IReadOnlyList<JobMergeTargetDto>> GetJobMergeTargetsAsync(long sourceJobId,CancellationToken ct);
    Task MergeJobAsync(long sourceJobId, MergeJobRequest request, long userId, CancellationToken ct);
    Task SetCandidateJobFavoriteAsync(long candidateJobId,bool favorite,long userId,CancellationToken ct);
    Task<FavoriteCandidateJobPage> ListFavoriteCandidateJobsAsync(int page,int pageSize,long? jobId,CancellationToken ct);
    Task<JobProfileDto?> GetJobProfileAsync(long jobId, CancellationToken ct);
    Task UpdateJobProfileAsync(long jobId, SaveJobProfileRequest request, CancellationToken ct);
    Task<CandidatePage> ListCandidatesAsync(int page, int pageSize, string? keyword, string? status, CancellationToken ct);
    Task<CandidateDetailDto?> GetCandidateAsync(long id, CancellationToken ct);
    Task UpdateCandidateAsync(long id, UpdateCandidateRequest request, CancellationToken ct);
    Task<ResumeDownloadInfo?> GetResumeDownloadAsync(long resumeId, CancellationToken ct);
}

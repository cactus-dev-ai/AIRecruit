namespace RecruitAI.Application.Interviews;

public sealed record InterviewListItemDto(
    long InvitationId, string InvitationStatus, DateTime ExpireTime, string? VerificationMethod,
    long QuestionSetId, string QuestionSetStatus, long CandidateJobId, long CandidateId,
    string CandidateName, long JobId, string JobName, long? SessionId, string? SessionStatus,
    DateTime? StartedTime, DateTime? CompletedTime, decimal? OverallScore, string? Recommendation,
    DateTime LastBusinessTime);

public sealed record InterviewListPage(IReadOnlyList<InterviewListItemDto> Items, int TotalCount, int Page, int PageSize);
public sealed record InterviewCandidateDto(long CandidateJobId, long CandidateId, string CandidateName, long JobId, string JobName, long ResumeId, decimal? AiScore);

public sealed record InterviewQuestionInput(
    long? Id, int SequenceNo, string QuestionType, string QuestionText, string? DimensionCode,
    int MaxAnswerSeconds, int MinTranscriptChars, bool IsScored, bool IsRequired);

public sealed record InterviewPlanDto(
    long Id, long JobId, string JobName, string Name, int VersionNo, string Status,
    int DurationMinutes, int MaxSessionMinutes, string ConsentTextVersion, DateTime UpdateTime,
    string RowVersion, IReadOnlyList<InterviewQuestionInput> Questions);

public sealed record SaveInterviewPlanRequest(
    long JobId, string Name, int DurationMinutes, int MaxSessionMinutes,
    string ConsentTextVersion, string? RowVersion, IReadOnlyList<InterviewQuestionInput> Questions);

public sealed record CreateQuestionSetRequest(long CandidateJobId, long PlanId);
public sealed record QuestionSetQuestionDto(
    long Id, int SequenceNo, string QuestionType, string QuestionText, string? DimensionCode,
    string? GenerationReason, string? ExpectedEvidenceJson, int MaxAnswerSeconds,
    bool IsRequired, bool IsScored, string Status, string RowVersion);

public sealed record QuestionSetDetailDto(
    long Id, string Status, int RevisionNo, long CandidateJobId, long CandidateId, string CandidateName,
    long JobId, string JobName, long PlanId, string PlanName, long ResumeId, DateTime UpdateTime,
    string RowVersion, IReadOnlyList<QuestionSetQuestionDto> Questions);

public sealed record UpdateQuestionSetRequest(string RowVersion, IReadOnlyList<QuestionSetQuestionDto> Questions);
public sealed record CreateInvitationRequest(long QuestionSetId, int ValidHours = 72, IReadOnlyList<string>? NotificationChannels = null);
public sealed record InvitationDeliveryDto(string Channel, string Status, string MaskedDestination, string? ErrorCode, string? ErrorMessage);
public sealed record CreatedInvitationDto(long InvitationId, string MiniProgramName, string FallbackCode, DateTime ExpireTime, string InvitationText, string H5Url, IReadOnlyList<InvitationDeliveryDto> Deliveries);

public sealed record InterviewTurnDto(long Id, int SequenceNo, int AttemptNo, string PromptText, string Status, string? Transcript, bool TranscriptIsFinal, int? DurationMs, string RowVersion);
public sealed record InterviewDetailDto(
    long InvitationId, string InvitationStatus, DateTime ExpireTime, string CandidateName, string? Phone,
    string? Email, string JobName, long QuestionSetId, string QuestionSetStatus, long? SessionId,
    string? SessionStatus, DateTime? StartedTime, DateTime? SubmittedTime, DateTime? CompletedTime,
    string? ErrorCode, string? ErrorMessage, decimal? OverallScore, string? ReportStatus,
    string? ReportSummary, string? Strengths, string? Concerns, string? Recommendation,
    string? ResumeKeyInfo, string? VoiceInterviewInfo, string? DimensionScoresJson,
    IReadOnlyList<InvitationDeliveryDto> Deliveries, IReadOnlyList<InterviewTurnDto> Turns);

public sealed record CorrectTranscriptRequest(string CorrectedTranscript, string Reason, string RowVersion);

public interface IInterviewManagementService
{
    Task<InterviewListPage> ListAsync(int page, int pageSize, string? status, string? keyword, CancellationToken ct);
    Task<IReadOnlyList<InterviewCandidateDto>> FindCandidatesAsync(string? keyword, int take, CancellationToken ct);
    Task<IReadOnlyList<InterviewPlanDto>> ListPlansAsync(long? jobId, CancellationToken ct);
    Task<InterviewPlanDto?> GetPlanAsync(long id, CancellationToken ct);
    Task<long> CreatePlanAsync(SaveInterviewPlanRequest request, long userId, CancellationToken ct);
    Task UpdatePlanAsync(long id, SaveInterviewPlanRequest request, CancellationToken ct);
    Task PublishPlanAsync(long id, long userId, CancellationToken ct);
    Task StopPlanAsync(long id, CancellationToken ct);
    Task<long> ClonePlanAsync(long id, long userId, CancellationToken ct);
    Task DeletePlanAsync(long id, CancellationToken ct);
    Task<long> CreateQuestionSetDraftAsync(CreateQuestionSetRequest request, CancellationToken ct);
    Task<QuestionSetDetailDto?> GetQuestionSetAsync(long id, CancellationToken ct);
    Task UpdateQuestionSetAsync(long id, UpdateQuestionSetRequest request, CancellationToken ct);
    Task ConfirmQuestionSetAsync(long id, long userId, CancellationToken ct);
    Task<CreatedInvitationDto> CreateInvitationAsync(CreateInvitationRequest request, long userId, CancellationToken ct);
    Task RevokeInvitationAsync(long id, long userId, CancellationToken ct);
    Task DeleteInvitationAsync(long id, long userId, CancellationToken ct);
    Task<InterviewDetailDto?> GetDetailAsync(long invitationId, CancellationToken ct);
    Task SubmitReportAsync(long sessionId, long userId, CancellationToken ct);
    Task CorrectTranscriptAsync(long turnId, CorrectTranscriptRequest request, long userId, CancellationToken ct);
    Task AbortSessionAsync(long sessionId, string reason, long userId, CancellationToken ct);
}

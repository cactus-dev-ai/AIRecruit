namespace RecruitAI.Application.Interviews;

public sealed record VerifyInterviewCodeRequest(string Phone, string Code);
public sealed record VerifiedInterviewDto(string AccessToken, DateTime ExpiresTime, string CandidateName, string JobName);
public sealed record StartPublicInterviewRequest(bool ConsentGranted, string? ClientInfo, string? DeviceCheckJson);
public sealed record PublicInterviewQuestionDto(long TurnId, int SequenceNo, string QuestionText, int MaxAnswerSeconds, bool IsRequired);
public sealed record PublicInterviewRoomDto(
    long SessionId, string SessionStatus, int SdkAppId, string RoomId, string UserId, string UserSig,
    IReadOnlyList<PublicInterviewQuestionDto> Questions);
public sealed record PublicTurnResultDto(long TurnId, string Status, string Transcript, int DurationMs);
public sealed record PublicInterviewReportDto(
    string Status, decimal? OverallScore, string Summary, string Strengths, string Concerns, string Recommendation);

public interface IPublicInterviewService
{
    Task<VerifiedInterviewDto> VerifyAsync(VerifyInterviewCodeRequest request, string? ip, string? clientInfo, CancellationToken ct);
    Task<PublicInterviewRoomDto> StartAsync(string accessToken, StartPublicInterviewRequest request, string? ip, CancellationToken ct);
    Task BeginRecordingAsync(string accessToken, CancellationToken ct);
    Task<PublicTurnResultDto> SubmitAnswerAsync(string accessToken, long turnId, Stream audio, long length, string contentType, int durationMs, CancellationToken ct);
    Task<PublicInterviewReportDto> SubmitAsync(string accessToken, CancellationToken ct);
}

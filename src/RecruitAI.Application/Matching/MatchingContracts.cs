namespace RecruitAI.Application.Matching;

public sealed record ReviewCandidateJobRequest(decimal? ManualScore, string Status, string Reason, string RowVersion);

public interface IMatchingReviewService
{
    Task ReviewAsync(long candidateJobId, ReviewCandidateJobRequest request, long userId, CancellationToken cancellationToken);
}

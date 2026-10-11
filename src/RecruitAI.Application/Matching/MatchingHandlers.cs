namespace RecruitAI.Application.Matching;

public sealed class ReviewCandidateJobHandler(IMatchingReviewService service)
{
    public Task HandleAsync(long candidateJobId, ReviewCandidateJobRequest request, long userId, CancellationToken ct) => service.ReviewAsync(candidateJobId, request, userId, ct);
}

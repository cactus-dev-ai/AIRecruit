using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecruitAI.Application.Matching;
using RecruitAI.Application.Recruitment;
using RecruitAI.Web.Infrastructure;

namespace RecruitAI.Web.Controllers;

[ApiController]
[Authorize(Policy = "Recruitment")]
[Route("api/candidate-jobs")]
public sealed class CandidateJobsController(ReviewCandidateJobHandler handler,IRecruitmentService recruitment) : ControllerBase
{
    [HttpPatch("{id:long}/review")]
    [HttpPost("{id:long}/review")]
    public async Task<IActionResult> Review(long id, ReviewCandidateJobRequest request, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, request, User.GetUserId(), cancellationToken); return NoContent();
    }

    [HttpPost("{id:long}/favorite")]
    public async Task<IActionResult> Favorite(long id,CancellationToken cancellationToken)
    {
        await recruitment.SetCandidateJobFavoriteAsync(id,true,User.GetUserId(),cancellationToken); return NoContent();
    }

    [HttpPost("{id:long}/unfavorite")]
    public async Task<IActionResult> Unfavorite(long id,CancellationToken cancellationToken)
    {
        await recruitment.SetCandidateJobFavoriteAsync(id,false,User.GetUserId(),cancellationToken); return NoContent();
    }

    [HttpGet("favorites")]
    public Task<FavoriteCandidateJobPage> Favorites(int page=1,int pageSize=20,long? jobId=null,CancellationToken cancellationToken=default)
        =>recruitment.ListFavoriteCandidateJobsAsync(page,pageSize,jobId,cancellationToken);
}

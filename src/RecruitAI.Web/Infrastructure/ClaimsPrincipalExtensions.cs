using System.Security.Claims;
using RecruitAI.Application.Common;

namespace RecruitAI.Web.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public static long GetUserId(this ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id : throw new BusinessException("UNAUTHORIZED", "登录状态无效。", 401);
}

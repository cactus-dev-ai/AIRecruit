using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecruitAI.Application.Auth;
using RecruitAI.Web.Infrastructure;

namespace RecruitAI.Web.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthSignInService auth) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public Task<CurrentUserDto> Login(LoginRequest request, CancellationToken cancellationToken) =>
        auth.SignInAsync(HttpContext, request.UserName, request.Password, cancellationToken);

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [HttpGet("me")]
    public ActionResult<CurrentUserDto> Me() => new CurrentUserDto(
        User.GetUserId(), User.Identity?.Name ?? "", User.FindFirst("display_name")?.Value ?? "", User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "");

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await auth.ChangePasswordAsync(User.GetUserId(), request.CurrentPassword, request.NewPassword, cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecruitAI.Application.Common;
using RecruitAI.Web.Infrastructure;

namespace RecruitAI.Web.Controllers;

public sealed class AccountController(AuthSignInService auth) : Controller
{
    [AllowAnonymous]
    [HttpGet("/login")]
    public IActionResult Login(string? returnUrl = null) => View(model: returnUrl);

    [AllowAnonymous]
    [HttpPost("/login")]
    public async Task<IActionResult> Login(string userName, string password, string? returnUrl, CancellationToken cancellationToken)
    {
        try
        {
            await auth.SignInAsync(HttpContext, userName, password, cancellationToken);
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        }
        catch (BusinessException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message); return View(model: returnUrl);
        }
    }

    [AllowAnonymous]
    [HttpGet("/access-denied")]
    public IActionResult AccessDenied() => Content("无权访问。", "text/plain; charset=utf-8");
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using RecruitAI.Application.Auth;
using RecruitAI.Application.Common;
using RecruitAI.Domain.Users;

namespace RecruitAI.Web.Infrastructure;

public sealed class AuthSignInService(IUserRepository users, LoginAttemptLimiter limiter)
{
    private readonly PasswordHasher<AppUser> _hasher = new();

    public async Task<CurrentUserDto> SignInAsync(HttpContext context, string userName, string password, CancellationToken cancellationToken)
    {
        userName = userName.Trim();
        if (userName.Length is < 1 or > 100 || string.IsNullOrEmpty(password))
            throw new BusinessException("LOGIN_FAILED", "用户名或密码错误。", 401);
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!limiter.TryAcquire(ip, userName)) throw new BusinessException("LOGIN_RATE_LIMITED", "登录尝试过于频繁，请稍后再试。", 429);
        var user = await users.FindByUserNameAsync(userName.ToUpperInvariant(), cancellationToken);
        if (user is null || user.Status != "ACTIVE" || _hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
            throw new BusinessException("LOGIN_FAILED", "用户名或密码错误。", 401);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.UserName),
            new Claim("display_name", user.DisplayName), new Claim(ClaimTypes.Role, user.RoleCode)
        };
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
        await users.UpdateLastLoginAsync(user.Id, cancellationToken);
        return new(user.Id, user.UserName, user.DisplayName, user.RoleCode);
    }

    public async Task ChangePasswordAsync(long userId, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        if (newPassword.Length is < 8 or > 64 || !newPassword.Any(char.IsLetter) || !newPassword.Any(char.IsDigit))
            throw new BusinessException("PASSWORD_POLICY_FAILED", "新密码必须为8至64位并同时包含字母和数字。 ");
        var user = await users.FindByIdAsync(userId, cancellationToken) ?? throw new BusinessException("USER_NOT_FOUND", "用户不存在。", 404);
        if (_hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            throw new BusinessException("CURRENT_PASSWORD_INVALID", "当前密码错误。 ");
        await users.UpdatePasswordAsync(userId, _hasher.HashPassword(user, newPassword), cancellationToken);
    }
}

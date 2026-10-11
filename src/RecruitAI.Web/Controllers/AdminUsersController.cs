using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RecruitAI.Application.Auth;
using RecruitAI.Application.Common;
using RecruitAI.Domain.Users;
using RecruitAI.Web.Infrastructure;

namespace RecruitAI.Web.Controllers;

[ApiController]
[Authorize(Policy = "ADMIN")]
[Route("api/admin/users")]
public sealed partial class AdminUsersController(IUserRepository users) : ControllerBase
{
    private readonly PasswordHasher<AppUser> _hasher = new();

    [HttpGet]
    public Task<IReadOnlyList<UserSummaryDto>> List(CancellationToken cancellationToken) => users.ListAsync(cancellationToken);

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        ValidateUser(request.UserName, request.DisplayName, request.Email, request.Role); ValidatePassword(request.Password);
        var placeholder = new AppUser { Id = 0, RoleId = 0, UserName = request.UserName.Trim(), DisplayName = request.DisplayName.Trim(), PasswordHash = "", Email = request.Email?.Trim(), Status = "ACTIVE", RoleCode = request.Role.Trim().ToUpperInvariant() };
        var id = await users.CreateAsync(new(placeholder.UserName, placeholder.DisplayName, placeholder.Email, placeholder.RoleCode, _hasher.HashPassword(placeholder, request.Password)), cancellationToken);
        return Created($"/api/admin/users/{id}", new { id });
    }

    [HttpPut("{id:long}")]
    [HttpPost("{id:long}/update")]
    public async Task<IActionResult> Update(long id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        ValidateUser("valid", request.DisplayName, request.Email, request.Role);
        byte[] version; try { version = Convert.FromBase64String(request.RowVersion); } catch { throw new BusinessException("INVALID_ROW_VERSION", "并发版本格式无效。 "); }
        await users.UpdateAsync(id, request with { DisplayName = request.DisplayName.Trim(), Email = request.Email?.Trim(), Role = request.Role.Trim().ToUpperInvariant() }, version, cancellationToken); return NoContent();
    }

    [HttpPost("{id:long}/disable")]
    public async Task<IActionResult> Disable(long id, CancellationToken cancellationToken)
    {
        if (id == User.GetUserId()) throw new BusinessException("CANNOT_DISABLE_SELF", "不能禁用当前登录账号。", 409);
        await users.ChangeStatusAsync(id, "DISABLED", cancellationToken); return NoContent();
    }

    [HttpPost("{id:long}/enable")]
    public async Task<IActionResult> Enable(long id, CancellationToken cancellationToken)
    {
        await users.ChangeStatusAsync(id, "ACTIVE", cancellationToken); return NoContent();
    }

    [HttpPost("{id:long}/reset-password")]
    public async Task<IActionResult> ResetPassword(long id, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        ValidatePassword(request.NewPassword);
        var user = await users.FindByIdAsync(id, cancellationToken) ?? throw new BusinessException("USER_NOT_FOUND", "用户不存在。", 404);
        await users.UpdatePasswordAsync(id, _hasher.HashPassword(user, request.NewPassword), cancellationToken); return NoContent();
    }

    private static void ValidateUser(string userName, string displayName, string? email, string role)
    {
        userName = userName.Trim(); displayName = displayName.Trim(); role = role.Trim().ToUpperInvariant();
        if (!UserNamePattern().IsMatch(userName)) throw new BusinessException("INVALID_USERNAME", "用户名必须为1至100位字母、数字、点、短横线或下划线。 ");
        if (displayName.Length is < 1 or > 200) throw new BusinessException("INVALID_DISPLAY_NAME", "显示名必须为1至200字。 ");
        if (email?.Length > 254) throw new BusinessException("INVALID_EMAIL", "邮箱不能超过254字。 ");
        if (role is not ("ADMIN" or "HR")) throw new BusinessException("INVALID_ROLE", "角色只能是ADMIN或HR。 ");
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length is < 8 or > 64 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new BusinessException("PASSWORD_POLICY_FAILED", "密码必须为8至64位并同时包含字母和数字。 ");
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex UserNamePattern();
}

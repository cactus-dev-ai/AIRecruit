using RecruitAI.Domain.Users;

namespace RecruitAI.Application.Auth;

public interface IUserRepository
{
    Task<AppUser?> FindByUserNameAsync(string normalizedUserName, CancellationToken cancellationToken);
    Task<AppUser?> FindByIdAsync(long id, CancellationToken cancellationToken);
    Task UpdateLastLoginAsync(long id, CancellationToken cancellationToken);
    Task UpdatePasswordAsync(long id, string passwordHash, CancellationToken cancellationToken);
    Task EnsureInitialAdminAsync(string passwordHash, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserSummaryDto>> ListAsync(CancellationToken cancellationToken);
    Task<long> CreateAsync(CreateUserData data, CancellationToken cancellationToken);
    Task UpdateAsync(long id, UpdateUserRequest request, byte[] rowVersion, CancellationToken cancellationToken);
    Task ChangeStatusAsync(long id, string status, CancellationToken cancellationToken);
}

public sealed record LoginRequest(string UserName, string Password);
public sealed record CurrentUserDto(long Id, string UserName, string DisplayName, string Role);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record UserSummaryDto(long Id, string UserName, string DisplayName, string? Email, string Role, string Status, string RowVersion);
public sealed record CreateUserRequest(string UserName, string DisplayName, string? Email, string Role, string Password);
public sealed record CreateUserData(string UserName, string DisplayName, string? Email, string Role, string PasswordHash);
public sealed record UpdateUserRequest(string DisplayName, string? Email, string Role, string RowVersion);
public sealed record ResetPasswordRequest(string NewPassword);

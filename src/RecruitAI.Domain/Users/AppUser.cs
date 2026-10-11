namespace RecruitAI.Domain.Users;

public sealed class AppUser
{
    public long Id { get; init; }
    public long RoleId { get; init; }
    public required string UserName { get; init; }
    public required string DisplayName { get; init; }
    public required string PasswordHash { get; set; }
    public string? Email { get; init; }
    public required string Status { get; init; }
    public required string RoleCode { get; init; }
}

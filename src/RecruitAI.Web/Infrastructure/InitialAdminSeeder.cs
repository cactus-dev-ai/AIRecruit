using Microsoft.AspNetCore.Identity;
using RecruitAI.Application.Auth;
using RecruitAI.Domain.Users;

namespace RecruitAI.Web.Infrastructure;

public sealed class InitialAdminSeeder(IServiceProvider serviceProvider, ILogger<InitialAdminSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var placeholder = new AppUser { Id = 0, RoleId = 0, UserName = "admin", DisplayName = "系统管理员", PasswordHash = "", Status = "ACTIVE", RoleCode = "ADMIN" };
            var hash = new PasswordHasher<AppUser>().HashPassword(placeholder, "123456");
            await repository.EnsureInitialAdminAsync(hash, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Initial ADMIN seed failed. Apply V001/V002 and verify database connectivity.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

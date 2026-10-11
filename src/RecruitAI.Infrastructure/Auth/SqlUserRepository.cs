using Microsoft.Data.SqlClient;
using RecruitAI.Application.Auth;
using RecruitAI.Domain.Users;
using RecruitAI.Infrastructure.Database;

namespace RecruitAI.Infrastructure.Auth;

public sealed class SqlUserRepository(SqlConnectionFactory connectionFactory) : IUserRepository
{
    public async Task<AppUser?> FindByUserNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) u.Id,u.RoleId,u.UserName,u.DisplayName,u.PasswordHash,u.Email,u.Status,r.Code
            FROM dbo.AppUser u
            JOIN dbo.Role r ON r.Id=u.RoleId
            WHERE UPPER(u.UserName)=@UserName
            """;
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@UserName", normalizedUserName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<AppUser?> FindByIdAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT u.Id,u.RoleId,u.UserName,u.DisplayName,u.PasswordHash,u.Email,u.Status,r.Code
            FROM dbo.AppUser u JOIN dbo.Role r ON r.Id=u.RoleId WHERE u.Id=@Id
            """;
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task UpdateLastLoginAsync(long id, CancellationToken cancellationToken)
    {
        await ExecuteAsync("UPDATE dbo.AppUser SET LastLoginTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME() WHERE Id=@Id", id, null, cancellationToken);
    }

    public async Task UpdatePasswordAsync(long id, string passwordHash, CancellationToken cancellationToken)
    {
        await ExecuteAsync("UPDATE dbo.AppUser SET PasswordHash=@PasswordHash,UpdateTime=SYSUTCDATETIME() WHERE Id=@Id", id, passwordHash, cancellationToken);
    }

    public async Task EnsureInitialAdminAsync(string passwordHash, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (
                SELECT 1 FROM dbo.AppUser u JOIN dbo.Role r ON r.Id=u.RoleId WHERE r.Code='ADMIN'
            )
            BEGIN
                INSERT dbo.AppUser(RoleId,UserName,DisplayName,PasswordHash,Email,Status)
                SELECT Id,N'admin',N'系统管理员',@PasswordHash,NULL,'ACTIVE'
                FROM dbo.Role WHERE Code='ADMIN';
            END
            """;
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@PasswordHash", System.Data.SqlDbType.NVarChar, 500).Value = passwordHash;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserSummaryDto>> ListAsync(CancellationToken cancellationToken)
    {
        const string sql = "SELECT u.Id,u.UserName,u.DisplayName,u.Email,r.Code,u.Status,u.RowVersion FROM dbo.AppUser u JOIN dbo.Role r ON r.Id=u.RoleId ORDER BY u.Id";
        await using var connection = await connectionFactory.OpenAsync(cancellationToken); await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var result = new List<UserSummaryDto>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5), Convert.ToBase64String((byte[])reader[6])));
        return result;
    }

    public async Task<long> CreateAsync(CreateUserData data, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.AppUser(RoleId,UserName,DisplayName,PasswordHash,Email,Status)
            SELECT Id,@UserName,@DisplayName,@PasswordHash,@Email,'ACTIVE' FROM dbo.Role WHERE Code=@Role;
            IF @@ROWCOUNT=0 RAISERROR(N'角色不存在。',16,1);
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
            """;
        await using var connection = await connectionFactory.OpenAsync(cancellationToken); await using var command = new SqlCommand(sql, connection);
        AddUserParameters(command, data.UserName, data.DisplayName, data.Email, data.Role); command.Parameters.Add("@PasswordHash", System.Data.SqlDbType.NVarChar, 500).Value = data.PasswordHash;
        try { return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { throw new RecruitAI.Application.Common.BusinessException("USERNAME_EXISTS", "用户名已存在。", 409); }
    }

    public async Task UpdateAsync(long id, UpdateUserRequest request, byte[] rowVersion, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE u SET DisplayName=@DisplayName,Email=@Email,RoleId=r.Id,UpdateTime=SYSUTCDATETIME()
            FROM dbo.AppUser u CROSS JOIN dbo.Role r
            WHERE u.Id=@Id AND u.RowVersion=@RowVersion AND r.Code=@Role;
            """;
        await using var connection = await connectionFactory.OpenAsync(cancellationToken); await using var command = new SqlCommand(sql, connection);
        AddUserParameters(command, "", request.DisplayName, request.Email, request.Role); command.Parameters.RemoveAt("@UserName");
        command.Parameters.AddWithValue("@Id", id); command.Parameters.Add("@RowVersion", System.Data.SqlDbType.Timestamp).Value = rowVersion;
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new RecruitAI.Application.Common.BusinessException("CONCURRENCY_CONFLICT", "用户已被修改或角色无效。", 409);
    }

    public async Task ChangeStatusAsync(long id, string status, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("UPDATE dbo.AppUser SET Status=@Status,UpdateTime=SYSUTCDATETIME() WHERE Id=@Id", connection);
        command.Parameters.AddWithValue("@Status", status); command.Parameters.AddWithValue("@Id", id);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new RecruitAI.Application.Common.BusinessException("USER_NOT_FOUND", "用户不存在。", 404);
    }

    private async Task ExecuteAsync(string sql, long id, string? passwordHash, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        if (passwordHash is not null)
            command.Parameters.Add("@PasswordHash", System.Data.SqlDbType.NVarChar, 500).Value = passwordHash;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static AppUser Map(SqlDataReader reader) => new()
    {
        Id = reader.GetInt64(0), RoleId = reader.GetInt64(1), UserName = reader.GetString(2),
        DisplayName = reader.GetString(3), PasswordHash = reader.GetString(4),
        Email = reader.IsDBNull(5) ? null : reader.GetString(5), Status = reader.GetString(6), RoleCode = reader.GetString(7)
    };

    private static void AddUserParameters(SqlCommand command, string userName, string displayName, string? email, string role)
    {
        command.Parameters.Add("@UserName", System.Data.SqlDbType.NVarChar, 100).Value = userName;
        command.Parameters.Add("@DisplayName", System.Data.SqlDbType.NVarChar, 200).Value = displayName;
        command.Parameters.Add("@Email", System.Data.SqlDbType.NVarChar, 254).Value = (object?)email ?? DBNull.Value;
        command.Parameters.AddWithValue("@Role", role);
    }
}

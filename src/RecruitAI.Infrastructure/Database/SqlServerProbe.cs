using Microsoft.Data.SqlClient;

namespace RecruitAI.Infrastructure.Database;

public sealed record SqlServerProbeResult(
    string Database,
    string ProductVersion,
    int TableCount);

public static class SqlServerProbe
{
    public static async Task<SqlServerProbeResult> CheckAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                DB_NAME(),
                CAST(SERVERPROPERTY('ProductVersion') AS varchar(30)),
                (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0)
            """;

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new SqlServerProbeResult(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2));
    }
}

using RecruitAI.Infrastructure.Database;

namespace RecruitAI.Tests;

public sealed class SqlServerCompatibilityTests
{
    [Fact]
    public async Task MicrosoftDataSqlClient_CanReadSqlServer2008R2()
    {
        const string connectionString =
            "Server=PC-HOMEC\\SQLEXPRESS;Database=RecruitAI;Integrated Security=true;" +
            "Encrypt=false;TrustServerCertificate=true";

        var result = await SqlServerProbe.CheckAsync(connectionString);

        Assert.Equal("RecruitAI", result.Database);
        Assert.StartsWith("10.50.", result.ProductVersion);
        Assert.True(result.TableCount > 0);
    }
}

namespace RecruitAI.Tests;

public sealed class V015MigrationTests
{
    private static string ReadMigration()
    {
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","database","V015__interview_invitation_delivery.sql"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void Migration_CreatesPerChannelInvitationDeliveryAudit()
    {
        var sql=ReadMigration();
        Assert.Contains("CREATE TABLE dbo.InterviewInvitationDelivery(",sql,StringComparison.Ordinal);
        Assert.Contains("UQ_InterviewInvitationDelivery_Invitation_Channel",sql,StringComparison.Ordinal);
        Assert.Contains("Channel IN('EMAIL','SMS')",sql,StringComparison.Ordinal);
        Assert.Contains("Status IN('SENT','FAILED')",sql,StringComparison.Ordinal);
        Assert.Contains("ProviderMessageId",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE OR ALTER",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ISJSON",sql,StringComparison.OrdinalIgnoreCase);
    }
}

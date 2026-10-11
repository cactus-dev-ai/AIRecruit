namespace RecruitAI.Tests;

public sealed class V012MigrationTests
{
    [Fact]
    public void Migration_CleansOnlyNonNullBlankCandidateContacts()
    {
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","database","V012__candidate_contact_null_normalization.sql"));
        var sql=File.ReadAllText(path);
        Assert.Contains("Phone IS NOT NULL",sql,StringComparison.Ordinal);
        Assert.Contains("PhoneNormalized IS NOT NULL",sql,StringComparison.Ordinal);
        Assert.Contains("Email IS NOT NULL",sql,StringComparison.Ordinal);
        Assert.Contains("EmailNormalized IS NOT NULL",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("ISNULL(Email",sql,StringComparison.OrdinalIgnoreCase);
    }
}

namespace RecruitAI.Tests;

public sealed class V011MigrationTests
{
    [Fact]
    public void Migration_AddsSharedCandidateJobFavoriteFieldsAndIndex()
    {
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","database","V011__resume_favorites.sql"));
        var sql=File.ReadAllText(path);
        Assert.Contains("IsFavorite",sql,StringComparison.Ordinal);
        Assert.Contains("FavoriteTime",sql,StringComparison.Ordinal);
        Assert.Contains("FavoriteBy",sql,StringComparison.Ordinal);
        Assert.Contains("IX_CandidateJob_Favorite",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE OR ALTER",sql,StringComparison.OrdinalIgnoreCase);
    }
}

using System.Text;
using RecruitAI.Infrastructure.Imports;

namespace RecruitAI.Tests;

public sealed class ResumeJsonValidatorTests
{
    [Fact]
    public void JsonWithoutContactInformation_IsAccepted()
    {
        var path = WriteTemp("""{"talent_id":"10001","source_route":"/Revision/talent/resume/detail","candidate_name":"测试候选人","headline":"电机工程师","visible_text":"五年电机设计经验","attachment_links":[]}""", "51JOB_10001.json");
        try { Assert.True(ResumeJsonValidator.IsValid(path, 10_485_760)); }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"candidate_name\":\"测试\"}")]
    [InlineData("{\"talent_id\":\"10001\",\"source_route\":\"/detail\",\"candidate_name\":\"\",\"visible_text\":\"内容\"}")]
    public void InvalidJsonResume_IsRejected(string json)
    {
        var path = WriteTemp(json, "51JOB_10001.json");
        try { Assert.False(ResumeJsonValidator.IsValid(path, 10_485_760)); }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void FileNameTalentIdMismatch_IsRejected()
    {
        var path = WriteTemp("""{"talent_id":"10001","source_route":"/detail","candidate_name":"测试","visible_text":"内容"}""", "51JOB_99999.json");
        try { Assert.False(ResumeJsonValidator.IsValid(path, 10_485_760)); }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void OriginalFileName_IsUsedAfterFileIsMovedToProcessingDirectory()
    {
        var path = WriteTemp("""{"talent_id":"10001","source_route":"/detail","candidate_name":"测试","visible_text":"内容"}""", "random-processing-name.json");
        try { Assert.True(ResumeJsonValidator.IsValid(path, 10_485_760, "51JOB_10001.json")); }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    private static string WriteTemp(string content, string fileName)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"recruitai-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }
}

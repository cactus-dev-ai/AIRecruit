using Job51.WinForms;

namespace Job51.Tests;

public sealed class ResumeTextCleanerTests
{
    [Fact]
    public void Clean_RemovesPageShellAndRepeatedWatermark_ButKeepsResumeSections()
    {
        const string company = "浙江鑫可传动科技有限公司";
        var source = string.Join('\n', new[]
        {
            "通知", "44", "手机版", "帮助", "企业管理", "人才ID:10001 活跃时间:2026.08.11",
            "扫码分享", "在线简历", "Resume", "测试候选人", "电话沟通", "求职意向", "机械工程师",
            "工作经历", "测试制造公司", $"{company}  {company}  {company}  {company}",
            "声明：以上人才信息仅供浙江鑫可传动科技有限公司招聘使用，禁止用于其他任何用途。",
            "操作动态", "暂无动态", "发送"
        });

        var cleaned = ResumeTextCleaner.Clean(source);

        Assert.Equal("测试候选人\n求职意向\n机械工程师\n工作经历\n测试制造公司", cleaned);
        Assert.Equal(cleaned, ResumeTextCleaner.Clean(cleaned));
    }

    [Fact]
    public void CleanCandidate_RemovesCompanyLabelAndRecoversMaskedPersonName()
    {
        var source = "鑫可传动\n谢先生\n24小时内活跃\n求职意向\n汽车设计工程师\n工作经历\n测试公司";

        var cleaned = ResumeTextCleaner.CleanCandidate("鑫可传动", source);

        Assert.Equal("谢先生", cleaned.CandidateName);
        Assert.Equal("谢先生\n24小时内活跃\n求职意向\n汽车设计工程师\n工作经历\n测试公司", cleaned.VisibleText);
    }
}

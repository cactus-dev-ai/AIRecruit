using RecruitAI.Infrastructure.Interviews;
using System.Net;
using System.Text;

namespace RecruitAI.Tests;

public sealed class InterviewInvitationNotifierTests
{
    private static InterviewInvitationNotifier CreateNotifier() => new(
        new HttpClient(),
        new TencentCloudSesSettings(false,"ses.tencentcloudapi.com","ap-guangzhou","","","noreply@example.com","面试邀请",57143,"code"),
        new AliyunSmsSettings(false,"dysmsapi.aliyuncs.com","","","","","job","inviteCode"));

    [Fact]
    public async Task EmailWithoutDestination_ReturnsAuditableFailure()
    {
        var result=await CreateNotifier().SendAsync(new(1,"EMAIL","候选人","工程师","","https://hr.example/interview","ABC12345",DateTime.UtcNow.AddDays(1)),default);
        Assert.False(result.Sent);
        Assert.Equal("EMAIL_MISSING",result.ErrorCode);
        Assert.Equal("未提供",result.MaskedDestination);
    }

    [Fact]
    public async Task SmsWhenProviderDisabled_DoesNotCallExternalService()
    {
        var result=await CreateNotifier().SendAsync(new(1,"SMS","候选人","工程师","13800138000","https://hr.example/interview","ABC12345",DateTime.UtcNow.AddDays(1)),default);
        Assert.False(result.Sent);
        Assert.Equal("SMS_NOT_CONFIGURED",result.ErrorCode);
        Assert.Equal("138****8000",result.MaskedDestination);
    }

    [Fact]
    public async Task TencentSes_UsesConfiguredTemplateAndCodeKey()
    {
        var handler=new StubHandler("{\"Response\":{\"MessageId\":\"ses-1\",\"RequestId\":\"request-1\"}}");
        var notifier=new InterviewInvitationNotifier(new HttpClient(handler),
            new TencentCloudSesSettings(true,"ses.tencentcloudapi.com","ap-guangzhou","id","secret","noreply@mail.example.com","面试邀请",57143,"code"),
            new AliyunSmsSettings(false,"dysmsapi.aliyuncs.com","","","","","job","inviteCode"));

        var result=await notifier.SendAsync(new(1,"EMAIL","候选人","工程师","candidate@example.com","https://hr.example/interview","ABC12345",DateTime.UtcNow.AddDays(1)),default);

        Assert.True(result.Sent);
        Assert.Equal("TENCENT_SES",result.Provider);
        using var payload=System.Text.Json.JsonDocument.Parse(handler.Body);
        Assert.Equal(57143,payload.RootElement.GetProperty("Template").GetProperty("TemplateID").GetInt64());
        using var templateData=System.Text.Json.JsonDocument.Parse(payload.RootElement.GetProperty("Template").GetProperty("TemplateData").GetString()!);
        Assert.Equal("ABC12345",templateData.RootElement.GetProperty("code").GetString());
        Assert.Equal("ABC12345",templateData.RootElement.GetProperty("CODE").GetString());
        Assert.Equal("SendEmail",handler.Request!.Headers.GetValues("X-TC-Action").Single());
    }

    [Fact]
    public async Task AliyunSms_UsesV3HeadersAndTemplateCode()
    {
        var handler=new StubHandler("{\"Code\":\"OK\",\"Message\":\"OK\",\"BizId\":\"biz-1\",\"RequestId\":\"request-1\"}");
        var notifier=new InterviewInvitationNotifier(new HttpClient(handler),
            new TencentCloudSesSettings(false,"ses.tencentcloudapi.com","ap-guangzhou","","","","",0,"code"),
            new AliyunSmsSettings(true,"dysmsapi.aliyuncs.com","access-id","access-secret","鑫可招聘","SMS_123456","job","inviteCode"));

        var result=await notifier.SendAsync(new(1,"SMS","候选人","工程师","13800138000","https://hr.example/interview","ABC12345",DateTime.UtcNow.AddDays(1)),default);

        Assert.True(result.Sent);
        Assert.Equal("ALIYUN_SMS",result.Provider);
        Assert.Contains("TemplateCode=SMS_123456",handler.Request!.RequestUri!.Query);
        var query=System.Web.HttpUtility.ParseQueryString(handler.Request.RequestUri.Query);
        using var templateParam=System.Text.Json.JsonDocument.Parse(query["TemplateParam"]!);
        Assert.Equal("工程师",templateParam.RootElement.GetProperty("job").GetString());
        Assert.Equal("ABC12345",templateParam.RootElement.GetProperty("inviteCode").GetString());
        Assert.Equal("SendSms",handler.Request.Headers.GetValues("x-acs-action").Single());
        Assert.StartsWith("ACS3-HMAC-SHA256 Credential=access-id",handler.Request.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public void SmsDiagnostics_ExposeOnlySafeCredentialFingerprint()
    {
        var notifier=new InterviewInvitationNotifier(new HttpClient(),
            new TencentCloudSesSettings(false,"ses.tencentcloudapi.com","ap-guangzhou","","","","",0,"code"),
            new AliyunSmsSettings(true,"dysmsapi.aliyuncs.com","access-id","super-secret","鑫可传动科技","SMS_123456","job","inviteCode"));

        var diagnostics=notifier.GetSmsDiagnostics();

        Assert.Equal(12,diagnostics.AccessKeyIdFingerprint.Length);
        Assert.True(diagnostics.AccessKeySecretConfigured);
        Assert.Equal(6,diagnostics.SignNameLength);
        Assert.Equal("E991ABE58FAFE4BCA0E58AA8E7A791E68A80",diagnostics.SignNameUtf8Hex);
        Assert.DoesNotContain("access-id",System.Text.Json.JsonSerializer.Serialize(diagnostics));
        Assert.DoesNotContain("super-secret",System.Text.Json.JsonSerializer.Serialize(diagnostics));
    }

    private sealed class StubHandler(string responseBody):HttpMessageHandler
    {
        public HttpRequestMessage? Request {get;private set;}
        public string Body {get;private set;}="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            Request=request;
            Body=request.Content is null?"":await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(responseBody,Encoding.UTF8,"application/json")};
        }
    }
}

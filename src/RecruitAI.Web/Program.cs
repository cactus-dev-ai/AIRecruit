using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using RecruitAI.Application.Auth;
using RecruitAI.Application.Imports;
using RecruitAI.Application.Interviews;
using RecruitAI.Application.Knowledge;
using RecruitAI.Application.Matching;
using RecruitAI.Application.Recruitment;
using RecruitAI.Application.Tasks;
using RecruitAI.Infrastructure.Auth;
using RecruitAI.Infrastructure.Database;
using RecruitAI.Infrastructure.Imports;
using RecruitAI.Infrastructure.Interviews;
using RecruitAI.Infrastructure.Knowledge;
using RecruitAI.Infrastructure.Matching;
using RecruitAI.Infrastructure.Recruitment;
using RecruitAI.Infrastructure.Tasks;
using RecruitAI.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options => options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddDataProtection().SetApplicationName("RecruitAI.Web");
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "RecruitAI.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.Cookie.Name = "RecruitAI.Auth";
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnRedirectToLogin = context => ApiCookieResponse(context, StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context => ApiCookieResponse(context, StatusCodes.Status403Forbidden);
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ADMIN", policy => policy.RequireRole("ADMIN"));
    options.AddPolicy("Recruitment", policy => policy.RequireRole("ADMIN", "HR"));
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

var connectionString = builder.Configuration.GetConnectionString("RecruitAI")
    ?? throw new InvalidOperationException("Missing RecruitAI connection string.");
builder.Services.AddSingleton(new SqlConnectionFactory(connectionString));
builder.Services.AddScoped<IUserRepository, SqlUserRepository>();
builder.Services.AddScoped<IResumeImportService, SqlResumeImportService>();
builder.Services.AddScoped<IKnowledgeImportService, SqlKnowledgeImportService>();
builder.Services.AddScoped<IKnowledgeManagementService, SqlKnowledgeManagementService>();
builder.Services.AddScoped<KnowledgeAiDraftService>();
builder.Services.AddScoped<IMatchingReviewService, SqlMatchingReviewService>();
builder.Services.AddScoped<IRecruitmentService, SqlRecruitmentService>();
builder.Services.AddScoped<IInterviewManagementService, SqlInterviewManagementService>();
builder.Services.AddScoped<IPublicInterviewService, SqlPublicInterviewService>();
builder.Services.AddHttpClient<TencentInterviewGateway>();
builder.Services.AddHttpClient<InterviewInvitationNotifier>();
builder.Services.AddSingleton(new TencentInterviewSettings(
    builder.Configuration.GetValue("TencentInterview:Enabled", false),
    builder.Configuration["TencentInterview:SecretId"] ?? string.Empty,
    builder.Configuration["TencentInterview:SecretKey"] ?? string.Empty,
    builder.Configuration.GetValue("TencentInterview:SdkAppId", 0),
    builder.Configuration["TencentInterview:SdkSecretKey"] ?? string.Empty,
    builder.Configuration["TencentInterview:CosBucket"] ?? string.Empty,
    builder.Configuration["TencentInterview:CosRegion"] ?? "ap-shanghai",
    builder.Configuration["TencentInterview:AsrEngineModelType"] ?? "16k_zh"));
builder.Services.AddSingleton(new TencentCloudSesSettings(
    string.Equals(builder.Configuration["EmailProvider"], "TencentCloudApi", StringComparison.OrdinalIgnoreCase),
    builder.Configuration["TencentCloudSes:Endpoint"] ?? "ses.tencentcloudapi.com",
    builder.Configuration["TencentCloudSes:Region"] ?? "ap-guangzhou",
    string.IsNullOrWhiteSpace(builder.Configuration["TencentCloudSes:SecretId"]) ? builder.Configuration["TencentInterview:SecretId"] ?? string.Empty : builder.Configuration["TencentCloudSes:SecretId"]!,
    string.IsNullOrWhiteSpace(builder.Configuration["TencentCloudSes:SecretKey"]) ? builder.Configuration["TencentInterview:SecretKey"] ?? string.Empty : builder.Configuration["TencentCloudSes:SecretKey"]!,
    builder.Configuration["TencentCloudSes:FromEmailAddress"] ?? string.Empty,
    builder.Configuration["TencentCloudSes:Subject"] ?? "面试邀请",
    builder.Configuration.GetValue<long>("TencentCloudSes:TemplateId", 0),
    builder.Configuration["TencentCloudSes:TemplateCodeKey"] ?? "CODE"));
builder.Services.AddSingleton(new AliyunSmsSettings(
    string.Equals(builder.Configuration["SmsProvider"], "Aliyun", StringComparison.OrdinalIgnoreCase),
    builder.Configuration["AliyunSms:Endpoint"] ?? "dysmsapi.aliyuncs.com",
    builder.Configuration["AliyunSms:AccessKeyId"] ?? string.Empty,
    builder.Configuration["AliyunSms:AccessKeySecret"] ?? string.Empty,
    builder.Configuration["AliyunSms:SignName"] ?? string.Empty,
    builder.Configuration["AliyunSms:TemplateCode"] ?? string.Empty,
    builder.Configuration["AliyunSms:TemplateJobKey"] ?? "job",
    builder.Configuration["AliyunSms:TemplateInviteCodeKey"] ?? "inviteCode"));
builder.Services.AddSingleton(new InterviewManagementSettings(
    builder.Configuration["Interview:MiniProgramName"] ?? "鑫可招聘AI面试",
    builder.Configuration["Interview:FallbackCodeLookupKey"] ?? throw new InvalidOperationException("Missing Interview:FallbackCodeLookupKey."),
    builder.Configuration["Agent:BaseUrl"] ?? "http://127.0.0.1:8100",
    builder.Configuration["Interview:PublicUrl"] ?? "https://hr.siekon.com/interview"));
builder.Services.AddScoped<IAiTaskService, SqlAiTaskService>();
builder.Services.AddSingleton(new AiTaskCleanupSettings(
    builder.Configuration["FileStorage:ResumeRoot"] ?? @"C:\Zhaopin\Resume"));
builder.Services.AddSingleton(new AgentWorkerControlSettings(
    builder.Configuration["Agent:BaseUrl"] ?? "http://127.0.0.1:8100",
    builder.Configuration["Agent:PythonPath"] ?? @"C:\Zhaopin\ai-agent-service\.venv\Scripts\python.exe",
    builder.Configuration["Agent:WorkingDirectory"] ?? @"C:\Zhaopin\ai-agent-service"));
builder.Services.AddSingleton<AgentWorkerControlService>();
builder.Services.AddScoped<RejectImportFileHandler>();
builder.Services.AddScoped<ConfirmImportBatchHandler>();
builder.Services.AddScoped<CancelImportReviewHandler>();
builder.Services.AddScoped<PurgeFailedImportFileHandler>();
builder.Services.AddScoped<ReturnFailedImportFileHandler>();
builder.Services.AddScoped<UploadKnowledgeExcelHandler>();
builder.Services.AddScoped<UpdateKnowledgeImportRowHandler>();
builder.Services.AddScoped<ConfirmKnowledgeImportHandler>();
builder.Services.AddScoped<ReviewCandidateJobHandler>();
builder.Services.AddSingleton(new ResumeImportSettings(
    builder.Configuration["FileStorage:ResumeTempRoot"] ?? @"C:\Zhaopin\ResumeTemp",
    builder.Configuration.GetValue("ResumeImport:MaxFilesPerScan", 100),
      builder.Configuration.GetValue("ResumeImport:MinimumStableSeconds", 30),
      builder.Configuration.GetValue("ResumeImport:HardMaxSourceFileBytes", 524_288_000L),
      builder.Configuration.GetValue("ResumeImport:JsonMaxFileBytes", 10_485_760L),
      builder.Configuration.GetValue("ResumeImport:ManualMaxFileBytes", 20_971_520L),
      builder.Configuration.GetValue("ResumeImport:ManualMaxFilesPerUpload", 10)));
builder.Services.AddSingleton(new KnowledgeImportSettings(
    builder.Configuration.GetValue("KnowledgeImport:MaxFileBytes", 10_485_760L),
    builder.Configuration.GetValue("KnowledgeImport:MaxRows", 5000),
    builder.Configuration["KnowledgeImport:RequiredWorksheetName"] ?? "Knowledge"));
builder.Services.AddSingleton<LoginAttemptLimiter>();
builder.Services.AddScoped<AuthSignInService>();
builder.Services.AddHostedService<InitialAdminSeeder>();

var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/error");
app.UseStaticFiles();
app.UseRouting();
app.UseMiddleware<BusinessExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.Run();

static Task ApiCookieResponse(RedirectContext<CookieAuthenticationOptions> context, int status)
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { code = status == 401 ? "UNAUTHORIZED" : "FORBIDDEN" });
    }
    context.Response.Redirect(context.RedirectUri);
    return Task.CompletedTask;
}

public partial class Program;

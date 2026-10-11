using RecruitAI.Application.Common;

namespace RecruitAI.Web.Infrastructure;

public sealed class BusinessExceptionMiddleware(RequestDelegate next, ILogger<BusinessExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (BusinessException exception)
        {
            context.Response.StatusCode = exception.StatusCode;
            await context.Response.WriteAsJsonAsync(new { code = exception.Code, message = exception.Message });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled request error. TraceId={TraceId}", context.TraceIdentifier);
            context.Response.StatusCode = 500;
            var message = context.User.Identity?.IsAuthenticated == true && context.User.IsInRole("ADMIN")
                ? BuildAdminMessage(exception)
                : "系统内部错误。";
            await context.Response.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message, traceId = context.TraceIdentifier });
        }
    }

    private static string BuildAdminMessage(Exception exception)
    {
        var details=new List<string>();
        for(var current=exception;current is not null&&details.Count<3;current=current.InnerException)
        {
            var text=current.Message?.Trim();
            if(!string.IsNullOrWhiteSpace(text)&&!details.Contains(text,StringComparer.Ordinal))details.Add(text);
        }
        var result=$"{exception.GetType().Name}：{string.Join("；",details)}";
        return result[..Math.Min(result.Length,2000)];
    }
}

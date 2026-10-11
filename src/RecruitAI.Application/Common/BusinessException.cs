namespace RecruitAI.Application.Common;

public sealed class BusinessException : Exception
{
    public BusinessException(string code, string message, int statusCode = 400) : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}

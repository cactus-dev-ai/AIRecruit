using RecruitAI.Web.Infrastructure;

namespace RecruitAI.Tests;

public sealed class LoginAttemptLimiterTests
{
    [Fact]
    public void SameIpAndUser_AllowsTenAttemptsPerMinute()
    {
        var limiter = new LoginAttemptLimiter();
        for (var index = 0; index < 10; index++) Assert.True(limiter.TryAcquire("127.0.0.1", "admin"));
        Assert.False(limiter.TryAcquire("127.0.0.1", "admin"));
        Assert.True(limiter.TryAcquire("127.0.0.1", "hr01"));
        Assert.True(limiter.TryAcquire("127.0.0.2", "admin"));
    }
}

using System.Net;
using Microsoft.AspNetCore.Http;
using Sms.Api.RateLimiting;

namespace Sms.Infrastructure.Tests;

public sealed class LoginRateLimitMiddlewareTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(5, 4)]
    [InlineData(6, 8)]
    [InlineData(7, 10)]
    [InlineData(20, 10)]
    public void Policy_UsesProgressiveDelayCappedAtTenMinutes(long failures, int expectedMinutes)
    {
        var delay = LoginRateLimitPolicy.GetDelay(failures);

        if (expectedMinutes == 0)
            Assert.Null(delay);
        else
            Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), delay);
    }

    [Fact]
    public async Task BlockedLogin_ReturnsTooManyRequestsWithRetryAfter()
    {
        var limiter = new FakeLoginRateLimiter { RetryAfter = TimeSpan.FromSeconds(125) };
        var nextCalls = 0;
        var middleware = new LoginRateLimitMiddleware(_ =>
        {
            nextCalls++;
            return Task.CompletedTask;
        });
        var context = LoginContext("/api/v1/portal/auth/token");

        await middleware.InvokeAsync(context, limiter);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("125", context.Response.Headers.RetryAfter);
        Assert.Equal(0, nextCalls);
        Assert.Empty(limiter.Failures);
        Assert.Empty(limiter.Resets);
    }

    [Fact]
    public async Task FailedLogin_RecordsFailureUsingNormalizedIp()
    {
        var limiter = new FakeLoginRateLimiter();
        var middleware = new LoginRateLimitMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        });
        var context = LoginContext("/api/v1/admin/auth/token");
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.10");

        await middleware.InvokeAsync(context, limiter);

        Assert.Equal(new[] { "192.0.2.10" }, limiter.Failures);
        Assert.Empty(limiter.Resets);
    }

    [Fact]
    public async Task SuccessfulLogin_ResetsIpPenalty()
    {
        var limiter = new FakeLoginRateLimiter();
        var middleware = new LoginRateLimitMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        var context = LoginContext("/api/v1/auth/token");

        await middleware.InvokeAsync(context, limiter);

        Assert.Empty(limiter.Failures);
        Assert.Equal(new[] { "203.0.113.20" }, limiter.Resets);
    }

    [Fact]
    public async Task NonLoginRequest_DoesNotUseLoginLimiter()
    {
        var limiter = new FakeLoginRateLimiter { RetryAfter = TimeSpan.FromMinutes(10) };
        var nextCalls = 0;
        var middleware = new LoginRateLimitMiddleware(_ =>
        {
            nextCalls++;
            return Task.CompletedTask;
        });
        var context = LoginContext("/api/v1/portal/auth/refresh");

        await middleware.InvokeAsync(context, limiter);

        Assert.Equal(1, nextCalls);
        Assert.Equal(0, limiter.Checks);
        Assert.Empty(limiter.Failures);
        Assert.Empty(limiter.Resets);
    }

    private static DefaultHttpContext LoginContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.20");
        return context;
    }

    private sealed class FakeLoginRateLimiter : ILoginRateLimiter
    {
        public TimeSpan? RetryAfter { get; init; }
        public int Checks { get; private set; }
        public List<string> Failures { get; } = [];
        public List<string> Resets { get; } = [];

        public Task<TimeSpan?> GetRetryAfterAsync(string ipAddress, CancellationToken cancellationToken = default)
        {
            Checks++;
            return Task.FromResult(RetryAfter);
        }

        public Task<LoginFailureState> RecordFailureAsync(string ipAddress, CancellationToken cancellationToken = default)
        {
            Failures.Add(ipAddress);
            return Task.FromResult(new LoginFailureState(Failures.Count, null));
        }

        public Task ResetAsync(string ipAddress, CancellationToken cancellationToken = default)
        {
            Resets.Add(ipAddress);
            return Task.CompletedTask;
        }
    }
}

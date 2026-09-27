using System.Globalization;
using System.Net;

namespace Sms.Api.RateLimiting;

public sealed class LoginRateLimitMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ILoginRateLimiter limiter)
    {
        if (!IsLoginRequest(context))
        {
            await next(context);
            return;
        }

        var ipAddress = Normalize(context.Connection.RemoteIpAddress);
        var retryAfter = await limiter.GetRetryAfterAsync(ipAddress, context.RequestAborted);
        if (retryAfter is { } blockedFor)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(blockedFor.TotalSeconds))
                .ToString(CultureInfo.InvariantCulture);
            return;
        }

        await next(context);

        if (context.Response.StatusCode is >= 200 and < 300)
        {
            await limiter.ResetAsync(ipAddress, context.RequestAborted);
            return;
        }

        if (context.Response.StatusCode is StatusCodes.Status400BadRequest or StatusCodes.Status401Unauthorized)
            await limiter.RecordFailureAsync(ipAddress, context.RequestAborted);
    }

    private static bool IsLoginRequest(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method)) return false;

        var path = context.Request.Path;
        return path.Equals("/api/v1/auth/token", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/v1/portal/auth/token", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/v1/admin/auth/token", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(IPAddress? address)
    {
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return address.ToString();
    }
}

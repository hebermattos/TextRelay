using System.Security.Claims;
using Sms.Application.Administration;

namespace Sms.Api.RateLimiting;

public sealed class TenantRateLimitMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public async Task InvokeAsync(
        HttpContext context,
        ITenantRateLimitRepository settings,
        IRateLimitCounter counter)
    {
        var tenantValue = context.User.FindFirstValue("tenant_id");
        if (!Guid.TryParse(tenantValue, out var tenantId))
        {
            await next(context);
            return;
        }

        var login = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(login))
        {
            await next(context);
            return;
        }

        var limits = await settings.GetAsync(tenantId, context.RequestAborted);
        var aiRequest = context.Request.Path.StartsWithSegments("/api/v1/message-assistant");
        var smsRequest = context.Request.Method == HttpMethods.Post
            && (context.Request.Path.Equals("/api/v1/messages", StringComparison.OrdinalIgnoreCase)
                || context.Request.Path.StartsWithSegments("/api/v1/messages/send")
                || context.Request.Path.StartsWithSegments("/api/v1/messages/bulk"));

        var limit = aiRequest ? limits.OllamaRequestsPerMinute : smsRequest ? limits.SmsPerMinute : limits.RequestsPerMinute;
        var bucket = aiRequest ? "ai" : smsRequest ? "sms" : "api";
        var minute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var key = $"rate-limit:{tenantId:N}:{login}:{bucket}:{minute}";

        var count = await counter.IncrementAsync(key, Window, context.RequestAborted);
        if (count > limit)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "60";
            return;
        }

        await next(context);
    }

}

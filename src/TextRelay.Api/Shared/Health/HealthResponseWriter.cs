using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Sms.Api.Health;

public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var response = new
        {
            status = report.Status.ToString(),
            checkedAtUtc = DateTimeOffset.UtcNow,
            durationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            checks = report.Entries
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToDictionary(
                    entry => entry.Key,
                    entry => new
                    {
                        status = entry.Value.Status.ToString(),
                        durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
                        description = entry.Value.Description,
                        tags = entry.Value.Tags.OrderBy(tag => tag, StringComparer.Ordinal).ToArray()
                    },
                    StringComparer.Ordinal)
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions), context.RequestAborted);
    }
}

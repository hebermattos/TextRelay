using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sms.Api.Health;
using Sms.Application.OptOut;

namespace Sms.Infrastructure.Tests;

public sealed class UtilityBehaviorTests
{
    [Theory]
    [InlineData("+1 (555) 123-4567", "+15551234567")]
    [InlineData("5551234567", "+5551234567")]
    [InlineData("  +55 11 99999-9999  ", "+5511999999999")]
    public void PhoneNumberNormalizer_NormalizesSupportedFormats(string input, string expected) =>
        Assert.Equal(expected, PhoneNumberNormalizer.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234567")]
    [InlineData("1234567890123456")]
    [InlineData("+1.555.123.4567")]
    [InlineData("abc12345678")]
    public void PhoneNumberNormalizer_RejectsInvalidNumbers(string input) =>
        Assert.Throws<ArgumentException>(() => PhoneNumberNormalizer.Normalize(input));

    [Fact]
    public async Task HealthResponseWriter_WritesStableJsonWithSortedChecksAndTags()
    {
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["redis"] = new(HealthStatus.Healthy, "cache ok", TimeSpan.FromMilliseconds(2), null, new Dictionary<string, object>(), ["z", "a"]),
            ["database"] = new(HealthStatus.Degraded, "db slow", TimeSpan.FromMilliseconds(3), null, new Dictionary<string, object>(), ["sql"])
        };
        var report = new HealthReport(entries, TimeSpan.FromMilliseconds(5));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await HealthResponseWriter.WriteAsync(context, report);

        Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var root = document.RootElement;
        Assert.Equal("Degraded", root.GetProperty("status").GetString());
        Assert.Equal(5, root.GetProperty("durationMs").GetDouble());
        var checks = root.GetProperty("checks");
        Assert.Equal("Degraded", checks.GetProperty("database").GetProperty("status").GetString());
        Assert.Equal("cache ok", checks.GetProperty("redis").GetProperty("description").GetString());
        Assert.Equal("a", checks.GetProperty("redis").GetProperty("tags")[0].GetString());
        Assert.True(root.TryGetProperty("checkedAtUtc", out _));
    }
}

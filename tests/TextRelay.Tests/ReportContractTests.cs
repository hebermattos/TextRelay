using Sms.Application.Logs;
using Sms.Application.Reports;

namespace Sms.Infrastructure.Tests;

public sealed class ReportContractTests
{
    [Fact]
    public void ReportContractsExposeAllConsolidatedValues()
    {
        var provider = new SmsReportProviderSummary("Twilio", 10, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        var summary = new SmsReportSummary(10, 1, 2, 3, 4, 5, 6, 7, 8, 9, [provider]);
        Assert.Equal(10, summary.TotalMessages);
        Assert.Equal("Twilio", summary.ByProvider.Single().Provider);

        var tenantId = Guid.NewGuid();
        var tenant = new PlatformSmsReportTenantSummary(tenantId, "Tenant", 10, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        var platform = new PlatformSmsReportSummary(10, 1, 2, 3, 4, 5, 6, 7, 8, 9, [tenant]);
        Assert.Equal(tenantId, platform.ByTenant.Single().TenantId);
        Assert.Equal(9, platform.Pending);

        var userId = Guid.NewGuid();
        var updated = DateTimeOffset.UtcNow;
        var user = new UserSmsReportSummary(userId, "user", new DateOnly(2026, 9, 21), 10, 1, 2, 3, 4, 5, 6, 7, 8, 9, updated);
        Assert.Equal(userId, user.UserId);
        Assert.Equal(updated, user.UpdatedAtUtc);
        Assert.Equal(10, user.TotalMessages);
    }

    [Fact]
    public void LogContractsExposeCursorAndEntryValues()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var entry = new LogEntry(7, timestamp, "Information", "Action", "Sent SMS", null, null, null);
        var cursor = new LogCursor(timestamp, entry.Id);

        Assert.Equal(7, cursor.Id);
        Assert.Equal(timestamp, cursor.Timestamp);
        Assert.Equal("Sent SMS", entry.Message);
    }
}

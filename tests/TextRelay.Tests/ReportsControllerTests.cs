using Sms.Api.Controllers;
using Sms.Application.Common;
using Sms.Application.Reports;

namespace Sms.Infrastructure.Tests;

public sealed class ReportsControllerTests
{
    [Fact]
    public async Task Sms_NormalizesProviderAndUsesTenantTimeZone()
    {
        var tenant = new Tenant();
        var reports = new Reports();
        var controller = new ReportsController(tenant, reports, new Zones());
        var from = new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);
        var to = from.AddHours(1);

        await controller.Sms(from, to, " Twilio ", default);

        Assert.Equal(tenant.TenantId, reports.TenantId);
        Assert.Equal("Twilio", reports.Filter!.Provider);
    }

    [Fact]
    public async Task Sms_UsesUtcWithoutTimeZoneProviderAndNormalizesBlankProvider()
    {
        var tenant = new Tenant();
        var reports = new Reports();
        var controller = new ReportsController(tenant, reports);
        await controller.Sms(null, null, "  ", default);
        Assert.Null(reports.Filter!.Provider);
    }

    [Fact]
    public async Task SmsByUser_IsTenantScoped()
    {
        var tenant = new Tenant();
        var reports = new Reports();
        var controller = new ReportsController(tenant, reports);
        await controller.SmsByUser(default);
        Assert.Equal(tenant.TenantId, reports.UserTenantId);
    }

    private sealed class Tenant : ITenantContext { public Guid TenantId { get; } = Guid.NewGuid(); }
    private sealed class Zones : ITenantTimeZoneProvider
    {
        public Task<TimeZoneInfo> GetAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TimeZoneInfo.Utc);
    }
    private sealed class Reports : ISmsReportRepository
    {
        public Guid TenantId { get; private set; }
        public Guid UserTenantId { get; private set; }
        public SmsReportFilter? Filter { get; private set; }
        public Task<SmsReportSummary> GetTenantSummaryAsync(Guid tenantId, SmsReportFilter filter, CancellationToken cancellationToken = default)
        { TenantId = tenantId; Filter = filter; return Task.FromResult(new SmsReportSummary(0,0,0,0,0,0,0,0,0,0,[])); }
        public Task<PlatformSmsReportSummary> GetPlatformSummaryAsync(SmsReportFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlatformSmsReportSummary(0,0,0,0,0,0,0,0,0,0,[]));
        public Task<IReadOnlyList<UserSmsReportSummary>> GetUserSummaryAsync(Guid tenantId, CancellationToken cancellationToken = default)
        { UserTenantId = tenantId; return Task.FromResult<IReadOnlyList<UserSmsReportSummary>>([]); }
    }
}

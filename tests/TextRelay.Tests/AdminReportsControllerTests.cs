using Sms.Api.Controllers;
using Sms.Application.Reports;

namespace Sms.Infrastructure.Tests;

public sealed class AdminReportsControllerTests
{
    [Fact]
    public async Task Sms_PassesFiltersAndNormalizesProvider()
    {
        var repository = new Reports();
        var controller = new AdminReportsController(repository);
        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow;

        await controller.Sms(from, to, " Twilio ", default);
        Assert.Equal(from, repository.Filter!.From);
        Assert.Equal(to, repository.Filter.To);
        Assert.Equal("Twilio", repository.Filter.Provider);

        await controller.Sms(null, null, " ", default);
        Assert.Null(repository.Filter!.Provider);
    }

    private sealed class Reports : ISmsReportRepository
    {
        public SmsReportFilter? Filter { get; private set; }
        public Task<PlatformSmsReportSummary> GetPlatformSummaryAsync(SmsReportFilter filter, CancellationToken cancellationToken = default)
        { Filter = filter; return Task.FromResult(new PlatformSmsReportSummary(0,0,0,0,0,0,0,0,0,0,[])); }
        public Task<SmsReportSummary> GetTenantSummaryAsync(Guid tenantId, SmsReportFilter filter, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<UserSmsReportSummary>> GetUserSummaryAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

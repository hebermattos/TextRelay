namespace Sms.Application.Reports;

public interface ISmsReportRepository
{
    Task<SmsReportSummary> GetTenantSummaryAsync(Guid tenantId, SmsReportFilter filter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserSmsReportSummary>> GetUserSummaryAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<PlatformSmsReportSummary> GetPlatformSummaryAsync(SmsReportFilter filter, CancellationToken cancellationToken = default);
}

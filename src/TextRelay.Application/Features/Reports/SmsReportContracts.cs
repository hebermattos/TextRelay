 namespace Sms.Application.Reports;

public sealed record SmsReportFilter(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Provider);

public sealed record SmsReportSummary(
    long TotalMessages,
    long Scheduled,
    long Queued,
    long Sent,
    long Delivered,
    long Failed,
    long Received,
    long Outbound,
    long Inbound,
    long Pending,
    IReadOnlyList<SmsReportProviderSummary> ByProvider);

public sealed record SmsReportProviderSummary(
    string Provider,
    long TotalMessages,
    long Scheduled,
    long Queued,
    long Sent,
    long Delivered,
    long Failed,
    long Received,
    long Outbound,
    long Inbound,
    long Pending);

public sealed record PlatformSmsReportSummary(
    long TotalMessages,
    long Scheduled,
    long Queued,
    long Sent,
    long Delivered,
    long Failed,
    long Received,
    long Outbound,
    long Inbound,
    long Pending,
    IReadOnlyList<PlatformSmsReportTenantSummary> ByTenant);

public sealed record PlatformSmsReportTenantSummary(
    Guid TenantId,
    string TenantName,
    long TotalMessages,
    long Scheduled,
    long Queued,
    long Sent,
    long Delivered,
    long Failed,
    long Received,
    long Outbound,
    long Inbound,
    long Pending);

public sealed record UserSmsReportSummary(
    Guid UserId,
    string Username,
    DateOnly ReportDate,
    long TotalMessages,
    long Scheduled,
    long Queued,
    long Sent,
    long Delivered,
    long Failed,
    long Received,
    long Outbound,
    long Inbound,
    long Pending,
    DateTimeOffset UpdatedAtUtc);

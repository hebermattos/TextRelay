namespace Sms.Application.Logs;

public sealed record LogEntry(
    long Id,
    DateTimeOffset Timestamp,
    string Severity,
    string Category,
    string Message,
    string? TraceId,
    string? SpanId,
    string? Attributes);

public sealed record LogCursor(DateTimeOffset Timestamp, long Id);

public interface ILogEntryRepository
{
    Task<IReadOnlyList<LogEntry>> GetActivityAsync(
        Guid tenantId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        LogCursor? cursor,
        int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LogEntry>> GetSystemAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        LogCursor? cursor,
        int take,
        CancellationToken cancellationToken = default);
}

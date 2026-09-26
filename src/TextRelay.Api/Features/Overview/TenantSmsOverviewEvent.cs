namespace Sms.Infrastructure.Messaging;

public sealed record TenantSmsOverviewEvent(
    Guid EventId,
    Guid TenantId,
    Guid? UserId,
    Guid MessageId,
    string TenantName,
    string? Username,
    string Provider,
    int Direction,
    int QueueStatus,
    int Status,
    DateTimeOffset CreatedAtUtc,
    long OutboundDelta,
    long InboundDelta,
    long DeliveredDelta,
    long FailedDelta,
    long PendingDelta,
    DateTimeOffset OccurredAtUtc);

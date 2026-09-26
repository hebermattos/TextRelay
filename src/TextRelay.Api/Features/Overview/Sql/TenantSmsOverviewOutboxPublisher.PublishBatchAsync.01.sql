SELECT
    EventId,
    TenantId,
    UserId,
    MessageId,
    TenantName,
    Username,
    Provider,
    Direction,
    QueueStatus,
    Status,
    CreatedAtUtc,
    OutboundDelta,
    InboundDelta,
    DeliveredDelta,
    FailedDelta,
    PendingDelta,
    OccurredAtUtc
FROM TenantSmsOverviewOutbox
WHERE PublishedAtUtc IS NULL
ORDER BY SequenceNumber
LIMIT 100;

SELECT Id AS MessageId,
       TenantId,
       QueueStatus AS OriginalQueueStatus,
       UpdatedAt
FROM SmsMessages
WHERE QueueStatus = @NotQueued
   OR (QueueStatus = @Scheduled AND ScheduledAtUtc <= CURRENT_TIMESTAMP)
   OR (QueueStatus = @Queued
       AND ScheduledAtUtc IS NOT NULL
       AND UpdatedAt <= CURRENT_TIMESTAMP - INTERVAL '5 minutes')
ORDER BY COALESCE(ScheduledAtUtc, UpdatedAt, CreatedAt), Id
LIMIT 100;

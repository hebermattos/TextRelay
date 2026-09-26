UPDATE SmsMessages
SET QueueStatus = @Queued,
    UpdatedAt = @UpdatedAt
WHERE TenantId = @TenantId
  AND Id = @MessageId
  AND QueueStatus = @ExpectedQueueStatus
  AND UpdatedAt IS NOT DISTINCT FROM @ExpectedUpdatedAt;

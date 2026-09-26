UPDATE SmsMessages
SET QueueStatus = @ReleasedQueueStatus,
    UpdatedAt = @UpdatedAt
WHERE TenantId = @TenantId
  AND Id = @MessageId
  AND QueueStatus = @Queued;

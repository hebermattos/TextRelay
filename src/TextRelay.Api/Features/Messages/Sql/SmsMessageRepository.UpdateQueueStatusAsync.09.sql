UPDATE SmsMessages
SET QueueStatus=@QueueStatus,
    UpdatedAt=@UpdatedAt
WHERE TenantId=@TenantId AND Id=@Id;

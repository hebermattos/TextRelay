WITH changed AS
(
    UPDATE SmsMessages
    SET QueueStatus=@Processing, UpdatedAt=@UpdatedAt
    WHERE TenantId=@TenantId
      AND Id=@Id
      AND QueueStatus=@Queued
      AND Status=@Pending
    RETURNING Id
)
SELECT CASE WHEN EXISTS(SELECT 1 FROM changed) THEN 1 ELSE 0 END;

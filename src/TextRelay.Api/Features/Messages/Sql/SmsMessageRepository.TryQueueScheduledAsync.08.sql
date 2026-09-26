WITH candidate AS
(
    SELECT Id
    FROM SmsMessages
    WHERE TenantId=@TenantId
      AND Id=@Id
      AND ScheduledAtUtc IS NOT NULL
      AND
      (
          (QueueStatus=@Scheduled AND ScheduledAtUtc <= @UpdatedAt)
          OR
          (QueueStatus=@Queued AND UpdatedAt <= @UpdatedAt - INTERVAL '5 minutes')
      )
    FOR UPDATE
),
changed AS
(
    UPDATE SmsMessages m
    SET QueueStatus=@Queued, UpdatedAt=@UpdatedAt
    FROM candidate c
    WHERE m.TenantId=@TenantId AND m.Id=c.Id
    RETURNING m.Id
)
SELECT CASE WHEN EXISTS(SELECT 1 FROM changed) THEN 1 ELSE 0 END;

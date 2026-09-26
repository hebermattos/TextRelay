WITH candidate AS
(
    SELECT Id, Status AS PreviousStatus
    FROM SmsMessages
    WHERE TenantId=@TenantId
      AND Provider=@Provider
      AND ProviderMessageId=@ProviderMessageId
      AND
      (
          Status=@Pending
          OR (Status=@Sent AND @Status IN (@Delivered, @Failed))
      )
    FOR UPDATE
),
changed AS
(
    UPDATE SmsMessages m
    SET Status=@Status, UpdatedAt=@UpdatedAt
    FROM candidate c
    WHERE m.TenantId=@TenantId AND m.Id=c.Id
    RETURNING m.Id, c.PreviousStatus
)
INSERT INTO SmsMessageStatusHistory(Id, TenantId, MessageId, Status, CreatedAt)
SELECT gen_random_uuid(), @TenantId, Id, @Status, @UpdatedAt
FROM changed
WHERE PreviousStatus<>@Status;

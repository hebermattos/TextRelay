WITH candidate AS
(
    SELECT Id, Status AS PreviousStatus
    FROM SmsMessages
    WHERE TenantId=@TenantId AND Id=@Id
    FOR UPDATE
),
changed AS
(
    UPDATE SmsMessages m
    SET Status=@Status,
        ProviderMessageId=COALESCE(@ProviderMessageId, m.ProviderMessageId),
        UpdatedAt=@UpdatedAt
    FROM candidate c
    WHERE m.TenantId=@TenantId AND m.Id=c.Id
    RETURNING m.Id, c.PreviousStatus
)
INSERT INTO SmsMessageStatusHistory(Id, TenantId, MessageId, Status, CreatedAt)
SELECT gen_random_uuid(), @TenantId, Id, @Status, @UpdatedAt
FROM changed
WHERE PreviousStatus<>@Status;

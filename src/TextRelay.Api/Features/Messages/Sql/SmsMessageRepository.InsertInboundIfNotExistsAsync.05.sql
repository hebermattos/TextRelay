WITH inserted_message AS
(
    INSERT INTO SmsMessages
        (Id, TenantId, "From", "To", Body, Provider, ProviderMessageId, Direction, QueueStatus, Status, CreatedAt, ScheduledAtUtc, UpdatedAt)
    VALUES
        (@Id, @TenantId, @From, @To, @Body, @Provider, @ProviderMessageId, @Direction, @QueueStatus, @Status, @CreatedAt, @ScheduledAtUtc, @UpdatedAt)
    ON CONFLICT (TenantId, Provider, ProviderMessageId)
        WHERE ProviderMessageId IS NOT NULL
    DO NOTHING
    RETURNING TenantId, Id, Status, CreatedAt
)
INSERT INTO SmsMessageStatusHistory(Id, TenantId, MessageId, Status, CreatedAt)
SELECT gen_random_uuid(), TenantId, Id, Status, CreatedAt
FROM inserted_message;

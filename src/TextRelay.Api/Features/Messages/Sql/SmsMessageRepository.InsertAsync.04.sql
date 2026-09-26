WITH inserted_message AS
(
    INSERT INTO SmsMessages
        (Id, TenantId, UserId, "From", "To", Body, Provider, ProviderMessageId, Direction, QueueStatus, Status, CreatedAt, ScheduledAtUtc, UpdatedAt)
    VALUES
        (@Id, @TenantId, @UserId, @From, @To, @Body, @Provider, @ProviderMessageId, @Direction, @QueueStatus, @Status, @CreatedAt, @ScheduledAtUtc, @UpdatedAt)
    RETURNING TenantId, Id, Status, CreatedAt
)
INSERT INTO SmsMessageStatusHistory(Id, TenantId, MessageId, Status, CreatedAt)
SELECT gen_random_uuid(), TenantId, Id, Status, CreatedAt
FROM inserted_message;

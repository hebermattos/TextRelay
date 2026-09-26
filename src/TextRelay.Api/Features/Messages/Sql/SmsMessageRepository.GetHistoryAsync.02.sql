SELECT Id, TenantId, UserId, "From", "To", Body, Provider, ProviderMessageId, Direction, QueueStatus, Status, CreatedAt, ScheduledAtUtc, UpdatedAt
FROM SmsMessages
WHERE TenantId=@TenantId
ORDER BY CreatedAt DESC, Id DESC
OFFSET @Skip LIMIT @Take;

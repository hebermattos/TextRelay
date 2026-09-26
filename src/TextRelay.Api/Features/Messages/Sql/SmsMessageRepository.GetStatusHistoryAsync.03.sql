SELECT Id, MessageId, Status, CreatedAt
FROM SmsMessageStatusHistory
WHERE TenantId=@TenantId AND MessageId=@MessageId
ORDER BY CreatedAt, Id;

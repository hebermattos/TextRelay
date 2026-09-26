SELECT Id, PhoneNumber, Source, Reason, CreatedAt, UpdatedAt
FROM SmsOptOuts
WHERE TenantId=@TenantId
ORDER BY CreatedAt DESC, Id DESC
OFFSET @Skip LIMIT @Take;

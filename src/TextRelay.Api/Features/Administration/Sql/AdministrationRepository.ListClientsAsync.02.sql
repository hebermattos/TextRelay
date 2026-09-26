SELECT Id, ClientId, IsActive, CreatedAt
FROM ApiClients
WHERE TenantId=@TenantId
ORDER BY CreatedAt DESC, Id
OFFSET @Skip LIMIT @Take;

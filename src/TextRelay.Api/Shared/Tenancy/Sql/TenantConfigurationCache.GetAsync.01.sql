SELECT Id, Name, TimeZoneId, IsActive, CreatedAt
FROM Tenants
WHERE Id=@TenantId;

SELECT Id, ClientId, IsActive, CreatedAt
FROM ApiClients
WHERE TenantId=@TenantId
ORDER BY CreatedAt DESC, Id;

SELECT TenantId, Provider, AccountId, ApiSecret, FromNumber, IsDefault, IsActive, Settings
FROM TenantSmsProviders
WHERE TenantId=@TenantId
ORDER BY Provider;

SELECT TenantId, Provider, AccountId, ApiSecret, FromNumber, IsDefault, IsActive, Settings
FROM TenantSmsProviders
WHERE TenantId=@TenantId
ORDER BY Provider;

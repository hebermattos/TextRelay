SELECT TenantId, Provider, AccountId, ApiSecret, FromNumber, IsDefault, IsActive, Settings
FROM TenantSmsProviders
WHERE TenantId=@TenantId AND Provider=@Provider AND IsActive;

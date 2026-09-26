SELECT TenantId, Provider, AccountId, ApiSecret, FromNumber, IsDefault, IsActive, Settings
FROM TenantSmsProviders
WHERE Provider=@Provider
  AND AccountId=@AccountId
  AND FromNumber=@FromNumber
  AND IsActive;

SELECT Id, TenantId, Name, Provider, Status, WindowMinutes, IsActive, CreatedAt, UpdatedAt
FROM AlertRules
WHERE TenantId=@TenantId
  AND Id=@RuleId
  AND DeletedAt IS NULL;

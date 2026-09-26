SELECT Id, TenantId, Name, Provider, Status, WindowMinutes, IsActive, CreatedAt, UpdatedAt
FROM AlertRules
WHERE TenantId=@TenantId AND DeletedAt IS NULL
ORDER BY CreatedAt DESC;

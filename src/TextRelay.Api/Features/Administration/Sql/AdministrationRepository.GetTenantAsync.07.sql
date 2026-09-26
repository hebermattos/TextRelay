SELECT Id, Name, TimeZoneId, IsActive, CreatedAt
FROM Tenants
WHERE Id=@TenantId;

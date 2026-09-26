SELECT TimeZoneId
FROM Tenants
WHERE Id=@TenantId AND IsActive;

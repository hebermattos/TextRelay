SELECT Name, TimeZoneId
FROM Tenants
WHERE Id=@TenantId AND IsActive;

SELECT Provider AS Name, FromNumber, IsDefault
FROM TenantSmsProviders
WHERE TenantId=@TenantId AND IsActive
ORDER BY IsDefault DESC, Provider;

UPDATE Tenants
SET Name=@Name, TimeZoneId=@TimeZoneId, IsActive=@IsActive
WHERE Id=@TenantId;

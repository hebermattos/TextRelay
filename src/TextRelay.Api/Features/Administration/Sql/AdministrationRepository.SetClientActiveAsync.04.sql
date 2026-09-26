UPDATE ApiClients
SET IsActive=@IsActive, UpdatedAt=@Now
WHERE TenantId=@TenantId AND Id=@ClientId;

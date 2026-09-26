UPDATE PortalUsers
SET IsActive=@IsActive,
    UpdatedAt=CURRENT_TIMESTAMP
WHERE TenantId=@TenantId AND Id=@Id AND Context='tenant';

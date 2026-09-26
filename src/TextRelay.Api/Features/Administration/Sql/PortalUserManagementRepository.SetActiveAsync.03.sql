UPDATE PortalUsers
SET IsActive=@IsActive,
    UpdatedAt=CURRENT_TIMESTAMP
WHERE Id=@Id AND Context='platform';

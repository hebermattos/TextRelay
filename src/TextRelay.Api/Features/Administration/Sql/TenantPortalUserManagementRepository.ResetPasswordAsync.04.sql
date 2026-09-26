UPDATE PortalUsers
SET PasswordHash=@Hash,
    PasswordSalt=@Salt,
    PasswordIterations=@Iterations,
    UpdatedAt=CURRENT_TIMESTAMP
WHERE TenantId=@TenantId AND Id=@Id AND Context='tenant';

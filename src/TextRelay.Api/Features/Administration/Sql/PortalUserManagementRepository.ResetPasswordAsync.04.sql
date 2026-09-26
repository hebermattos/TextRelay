UPDATE PortalUsers
SET PasswordHash=@Hash,
    PasswordSalt=@Salt,
    PasswordIterations=@Iterations,
    UpdatedAt=CURRENT_TIMESTAMP
WHERE Id=@Id AND Context='platform';

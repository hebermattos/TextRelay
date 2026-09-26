INSERT INTO PortalUsers
    (Id, TenantId, Username, Email, PasswordHash, PasswordSalt, PasswordIterations, Context, Role, IsActive, CreatedAt)
VALUES
    (@Id, @TenantId, @Username, @Email, @PasswordHash, @PasswordSalt, @PasswordIterations, 'tenant', @Role, TRUE, CURRENT_TIMESTAMP);

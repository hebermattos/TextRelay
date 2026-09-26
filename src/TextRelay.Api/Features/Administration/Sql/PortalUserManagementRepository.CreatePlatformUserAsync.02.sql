INSERT INTO PortalUsers
    (Id, TenantId, Username, Email, PasswordHash, PasswordSalt, PasswordIterations, Context, Role, IsActive, CreatedAt)
VALUES
    (@Id, NULL, @Username, @Email, @PasswordHash, @PasswordSalt, @PasswordIterations, 'platform', @Role, TRUE, CURRENT_TIMESTAMP);

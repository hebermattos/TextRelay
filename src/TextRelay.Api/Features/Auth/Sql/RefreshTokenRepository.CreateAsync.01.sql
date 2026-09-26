INSERT INTO RefreshTokens
    (Id, UserId, FamilyId, Username, TenantId, Context, Role, TokenHash, ExpiresAt, CreatedAt)
VALUES
    (@Id, @UserId, @Id, @Username, @TenantId, @Context, @Role, @TokenHash, @ExpiresAt, CURRENT_TIMESTAMP);

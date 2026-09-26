INSERT INTO RefreshTokens
    (Id, UserId, FamilyId, Username, TenantId, Context, Role, TokenHash, ExpiresAt, CreatedAt)
SELECT
    @Id, @UserId, FamilyId, @Username, @TenantId, @Context, @Role, @TokenHash, @ExpiresAt, CURRENT_TIMESTAMP
FROM RefreshTokens
WHERE Id = @CurrentId;

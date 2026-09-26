SELECT Id, UserId, Username, TenantId, Context, Role, ExpiresAt
FROM RefreshTokens
WHERE TokenHash = @TokenHash AND RevokedAt IS NULL AND ExpiresAt > CURRENT_TIMESTAMP
FOR UPDATE;

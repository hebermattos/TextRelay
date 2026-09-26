UPDATE RefreshTokens
SET RevokedAt = CURRENT_TIMESTAMP
WHERE TokenHash = @TokenHash
  AND RevokedAt IS NULL
  AND ExpiresAt > CURRENT_TIMESTAMP;

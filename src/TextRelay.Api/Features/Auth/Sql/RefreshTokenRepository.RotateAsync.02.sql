UPDATE RefreshTokens
SET RevokedAt = CURRENT_TIMESTAMP, ReplacedByHash = @ReplacementHash
WHERE Id = @Id AND RevokedAt IS NULL;

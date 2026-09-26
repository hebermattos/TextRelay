UPDATE RefreshTokens
SET RevokedAt = COALESCE(RevokedAt, CURRENT_TIMESTAMP)
WHERE FamilyId = (
    SELECT FamilyId
    FROM RefreshTokens
    WHERE TokenHash = @TokenHash
)
AND EXISTS (
    SELECT 1
    FROM RefreshTokens reused
    WHERE reused.TokenHash = @TokenHash
      AND reused.RevokedAt IS NOT NULL
      AND reused.ReplacedByHash IS NOT NULL
);

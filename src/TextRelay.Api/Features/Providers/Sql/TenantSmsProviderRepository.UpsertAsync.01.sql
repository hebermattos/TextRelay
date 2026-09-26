SELECT pg_advisory_xact_lock(hashtextextended(@TenantId::text, 0));

UPDATE TenantSmsProviders
SET IsDefault=FALSE, UpdatedAt=@Now
WHERE TenantId=@TenantId
  AND Provider<>@Provider
  AND IsDefault
  AND @IsDefault
  AND @IsActive;

INSERT INTO TenantSmsProviders
    (Id, TenantId, Provider, AccountId, ApiSecret, FromNumber, IsDefault, IsActive, Settings, CreatedAt)
SELECT
    @Id, @TenantId, @Provider, @AccountId, @ApiSecret, @FromNumber, @IsDefault, @IsActive, @Settings, @Now
WHERE EXISTS (SELECT 1 FROM Tenants WHERE Id=@TenantId)
ON CONFLICT (TenantId, Provider)
DO UPDATE SET
    AccountId=EXCLUDED.AccountId,
    ApiSecret=EXCLUDED.ApiSecret,
    FromNumber=EXCLUDED.FromNumber,
    IsDefault=EXCLUDED.IsDefault,
    IsActive=EXCLUDED.IsActive,
    Settings=EXCLUDED.Settings,
    UpdatedAt=@Now;

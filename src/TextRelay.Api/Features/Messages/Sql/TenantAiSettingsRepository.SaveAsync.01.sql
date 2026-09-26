INSERT INTO TenantAiSettings (TenantId, ImprovePrompt, ValidatePrompt, UpdatedAt)
VALUES (@TenantId, @ImprovePrompt, @ValidatePrompt, @UpdatedAt)
ON CONFLICT (TenantId) DO UPDATE
SET ImprovePrompt=EXCLUDED.ImprovePrompt, ValidatePrompt=EXCLUDED.ValidatePrompt, UpdatedAt=EXCLUDED.UpdatedAt;
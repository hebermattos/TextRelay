UPDATE AlertRules
SET IsActive=FALSE,
    IsTriggered=FALSE,
    DeletedAt=CURRENT_TIMESTAMP,
    UpdatedAt=CURRENT_TIMESTAMP
WHERE TenantId=@TenantId
  AND Id=@Id
  AND DeletedAt IS NULL;

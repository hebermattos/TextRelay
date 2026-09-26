UPDATE AlertRules
SET Name=@Name,
    Provider=@Provider,
    Status=@Status,
    WindowMinutes=@WindowMinutes,
    IsActive=@IsActive,
    UpdatedAt=CURRENT_TIMESTAMP
WHERE TenantId=@TenantId
  AND Id=@Id
  AND DeletedAt IS NULL;

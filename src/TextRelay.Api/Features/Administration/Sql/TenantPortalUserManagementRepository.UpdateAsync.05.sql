UPDATE PortalUsers
SET Username=@Username,
    Email=@Email,
    Role=@Role,
    UpdatedAt=NOW()
WHERE Id=@Id
  AND TenantId=@TenantId
  AND Context='tenant';

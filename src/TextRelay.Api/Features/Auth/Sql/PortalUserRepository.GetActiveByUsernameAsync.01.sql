SELECT u.Id, u.TenantId, u.Username, u.Email, u.PasswordHash, u.PasswordSalt, u.PasswordIterations,
       u.Context, u.Role, u.IsActive
FROM PortalUsers u
LEFT JOIN Tenants t ON t.Id=u.TenantId
WHERE LOWER(u.Username)=LOWER(@Username)
  AND u.Context=@Context
  AND u.IsActive
  AND
  (
      (@Context='platform' AND u.TenantId IS NULL)
      OR
      (@Context='tenant' AND t.Code=@TenantCode AND t.IsActive)
  );

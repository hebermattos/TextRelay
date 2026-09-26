SELECT Id, TenantId, Username, Email, PasswordHash, PasswordSalt, PasswordIterations, Context, Role, IsActive
FROM PortalUsers u
WHERE u.Id=@Id
  AND u.IsActive
  AND
  (
      u.Context='platform'
      OR EXISTS
      (
          SELECT 1
          FROM Tenants t
          WHERE t.Id=u.TenantId AND t.IsActive
      )
  );

SELECT c.TenantId, c.ClientId, c.SecretHash, c.SecretSalt, c.SecretIterations
FROM ApiClients c
INNER JOIN Tenants t ON t.Id=c.TenantId
WHERE c.ClientId=@ClientId
  AND c.IsActive
  AND t.IsActive;

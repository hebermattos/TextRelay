UPDATE ApiClients
SET SecretHash=@Hash, SecretSalt=@Salt, SecretIterations=@Iterations, UpdatedAt=@Now
WHERE TenantId=@TenantId AND Id=@ClientId
RETURNING ClientId;

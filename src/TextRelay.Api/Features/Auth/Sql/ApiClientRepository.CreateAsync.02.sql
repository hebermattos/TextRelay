INSERT INTO ApiClients
    (Id, TenantId, ClientId, SecretHash, SecretSalt, SecretIterations, IsActive, CreatedAt)
VALUES
    (@Id, @TenantId, @ClientId, @SecretHash, @SecretSalt, @SecretIterations, TRUE, @CreatedAt);

INSERT INTO AlertRules
    (Id, TenantId, Name, Provider, Status, WindowMinutes, IsActive, CreatedAt)
VALUES
    (@Id, @TenantId, @Name, @Provider, @Status, @WindowMinutes, @IsActive, @CreatedAt);

INSERT INTO Tenants(Id, Name, TimeZoneId, IsActive, CreatedAt)
VALUES(@Id, @Name, 'UTC', TRUE, @CreatedAt);

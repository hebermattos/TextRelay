INSERT INTO Tenants(Id, Name, Code, TimeZoneId, IsActive, CreatedAt)
VALUES(@Id, @Name, @Code, 'UTC', TRUE, @Now);

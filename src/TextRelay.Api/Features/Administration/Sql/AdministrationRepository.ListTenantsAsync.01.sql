SELECT Id, Name, TimeZoneId, IsActive, CreatedAt
FROM Tenants
ORDER BY CreatedAt DESC, Id
OFFSET @Skip LIMIT @Take;

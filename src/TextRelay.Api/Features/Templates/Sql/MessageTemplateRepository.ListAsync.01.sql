SELECT Id AS "Id", Name AS "Name", Body AS "Body", CreatedAt AS "CreatedAt", UpdatedAt AS "UpdatedAt"
FROM MessageTemplates WHERE TenantId = @TenantId
ORDER BY Name, Id OFFSET @Skip LIMIT @Take;

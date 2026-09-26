SELECT Id AS "Id", Name AS "Name", Body AS "Body", CreatedAt AS "CreatedAt", UpdatedAt AS "UpdatedAt"
FROM MessageTemplates WHERE TenantId = @TenantId AND Id = @Id;

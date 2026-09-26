UPDATE MessageTemplates SET Name = @Name, Body = @Body, UpdatedAt = CURRENT_TIMESTAMP
WHERE TenantId = @TenantId AND Id = @Id
RETURNING Id AS "Id", Name AS "Name", Body AS "Body", CreatedAt AS "CreatedAt", UpdatedAt AS "UpdatedAt";

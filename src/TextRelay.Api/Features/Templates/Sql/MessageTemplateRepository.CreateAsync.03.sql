INSERT INTO MessageTemplates (Id, TenantId, Name, Body, CreatedAt)
VALUES (@Id, @TenantId, @Name, @Body, CURRENT_TIMESTAMP)
RETURNING Id AS "Id", Name AS "Name", Body AS "Body", CreatedAt AS "CreatedAt", UpdatedAt AS "UpdatedAt";

SELECT Id, TenantId, Username, Email, Context, Role, IsActive, CreatedAt
FROM PortalUsers
WHERE TenantId = @TenantId
  AND Context = 'tenant'
  AND (@Search IS NULL OR Username ILIKE '%' || @Search || '%' OR Email ILIKE '%' || @Search || '%')
  AND (@Role IS NULL OR Role = @Role)
  AND (@IsActive IS NULL OR IsActive = @IsActive)
ORDER BY Username, Id
OFFSET @Skip
LIMIT @Take;

SELECT Id, RuleId, RuleName, Provider, Status, MatchCount, WindowMinutes, CreatedAt, IsRead, ReadAt
FROM Alerts
WHERE TenantId=@TenantId
  AND (NOT @UnreadOnly OR NOT IsRead)
ORDER BY CreatedAt DESC, Id DESC
OFFSET @Skip LIMIT @Take;

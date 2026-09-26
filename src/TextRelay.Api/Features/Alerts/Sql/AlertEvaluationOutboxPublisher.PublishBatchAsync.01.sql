SELECT Id AS EventId, TenantId, Provider, Status, OccurredAtUtc
FROM AlertEvaluationOutbox
WHERE PublishedAtUtc IS NULL
ORDER BY CreatedAtUtc, Id
LIMIT 100;

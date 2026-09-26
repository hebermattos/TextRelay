INSERT INTO Alerts
    (Id, TenantId, RuleId, EventId, RuleName, Provider, Status, MatchCount, WindowMinutes, CreatedAt, IsRead)
VALUES
    (gen_random_uuid(), @TenantId, @RuleId, @EventId, @RuleName, @Provider, @Status, @MatchCount, @WindowMinutes, @OccurredAtUtc, FALSE)
ON CONFLICT (TenantId, RuleId, EventId) DO NOTHING;

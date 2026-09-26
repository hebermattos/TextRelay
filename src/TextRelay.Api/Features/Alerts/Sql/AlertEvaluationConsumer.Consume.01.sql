INSERT INTO AlertMessageWindow(EventId, TenantId, Provider, Status, OccurredAtUtc, ExpiresAtUtc)
VALUES(@EventId, @TenantId, @Provider, @Status, @OccurredAtUtc, @OccurredAtUtc + INTERVAL '24 hours')
ON CONFLICT (EventId) DO NOTHING;

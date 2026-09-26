UPDATE TenantSmsOverviewOutbox
SET PublishedAtUtc=CURRENT_TIMESTAMP,
    AttemptCount=AttemptCount+1,
    LastAttemptAtUtc=CURRENT_TIMESTAMP
WHERE EventId=@EventId AND PublishedAtUtc IS NULL;

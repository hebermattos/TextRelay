UPDATE AlertEvaluationOutbox
SET PublishedAtUtc=CURRENT_TIMESTAMP,
    AttemptCount=AttemptCount+1,
    LastAttemptAtUtc=CURRENT_TIMESTAMP
WHERE Id=@Id AND PublishedAtUtc IS NULL;

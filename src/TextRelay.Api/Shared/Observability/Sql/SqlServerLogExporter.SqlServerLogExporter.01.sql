INSERT INTO UserActivityLogs
    ("Timestamp", TenantId, Severity, Category, Message, TraceId, SpanId, Attributes)
VALUES
    (@Timestamp, @TenantId, @Severity, @Category, @Message, @TraceId, @SpanId, @Attributes);

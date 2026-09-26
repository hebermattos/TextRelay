INSERT INTO SystemLogs
    ("Timestamp", Severity, Category, Message, TraceId, SpanId, Attributes)
VALUES
    (@Timestamp, @Severity, @Category, @Message, @TraceId, @SpanId, @Attributes);

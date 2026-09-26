SELECT
    Provider,
    SUM(TotalMessages)::BIGINT AS TotalMessages,
    SUM(Scheduled)::BIGINT AS Scheduled,
    SUM(Queued)::BIGINT AS Queued,
    SUM(Sent)::BIGINT AS Sent,
    SUM(Delivered)::BIGINT AS Delivered,
    SUM(Failed)::BIGINT AS Failed,
    SUM(Received)::BIGINT AS Received,
    SUM(Outbound)::BIGINT AS Outbound,
    SUM(Inbound)::BIGINT AS Inbound,
    SUM(Pending)::BIGINT AS Pending
FROM ProviderSmsDailyOverview
WHERE TenantId=@TenantId
  AND (@From IS NULL OR ReportDate >= CAST(@From AT TIME ZONE 'UTC' AS date))
  AND (@To IS NULL OR ReportDate < CAST(@To AT TIME ZONE 'UTC' AS date))
  AND (@Provider IS NULL OR Provider=@Provider)
GROUP BY Provider
ORDER BY Provider;

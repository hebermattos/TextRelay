SELECT
    UserId, Username, ReportDate,
    TotalMessages, Scheduled, Queued, Sent, Delivered, Failed, Received, Outbound, Inbound, Pending, UpdatedAtUtc
FROM UserSmsOverview
WHERE TenantId=@TenantId
ORDER BY ReportDate DESC, TotalMessages DESC, Username, UserId;

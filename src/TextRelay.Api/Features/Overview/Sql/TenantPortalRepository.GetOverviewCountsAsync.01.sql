SELECT Outbound, Inbound, Delivered, Failed, Pending
FROM TenantSmsOverview
WHERE TenantId=@TenantId;

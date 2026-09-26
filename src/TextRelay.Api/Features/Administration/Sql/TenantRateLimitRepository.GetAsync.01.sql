SELECT RequestsPerMinute, SmsPerMinute, OllamaRequestsPerMinute
FROM TenantRateLimits
WHERE TenantId = @TenantId;

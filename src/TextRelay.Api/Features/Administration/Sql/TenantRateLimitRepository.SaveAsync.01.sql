INSERT INTO TenantRateLimits (TenantId, RequestsPerMinute, SmsPerMinute, OllamaRequestsPerMinute, UpdatedAt)
VALUES (@TenantId, @RequestsPerMinute, @SmsPerMinute, @OllamaRequestsPerMinute, CURRENT_TIMESTAMP)
ON CONFLICT (TenantId) DO UPDATE
SET RequestsPerMinute = EXCLUDED.RequestsPerMinute,
    SmsPerMinute = EXCLUDED.SmsPerMinute,
    OllamaRequestsPerMinute = EXCLUDED.OllamaRequestsPerMinute,
    UpdatedAt = CURRENT_TIMESTAMP;

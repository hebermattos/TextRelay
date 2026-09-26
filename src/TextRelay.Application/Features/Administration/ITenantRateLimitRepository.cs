namespace Sms.Application.Administration;

public sealed record TenantRateLimitSettings(int RequestsPerMinute, int SmsPerMinute, int OllamaRequestsPerMinute);

public interface ITenantRateLimitRepository
{
    Task<TenantRateLimitSettings> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task SaveAsync(Guid tenantId, TenantRateLimitSettings settings, CancellationToken cancellationToken = default);
}

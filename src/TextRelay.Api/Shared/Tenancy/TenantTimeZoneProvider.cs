using Sms.Application.Common;

namespace Sms.Infrastructure.Persistence;

public sealed class TenantTimeZoneProvider(TenantConfigurationCache configurationCache) : ITenantTimeZoneProvider
{
    public async Task<TimeZoneInfo> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var snapshot = await configurationCache.GetAsync(tenantId, cancellationToken);
        if (snapshot is null) throw new KeyNotFoundException();
        return TimeZoneInfo.FindSystemTimeZoneById(snapshot.Tenant.TimeZoneId);
    }
}

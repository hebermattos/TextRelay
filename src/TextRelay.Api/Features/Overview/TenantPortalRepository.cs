using Dapper;
using Sms.Application.Administration;

namespace Sms.Infrastructure.Persistence;

public sealed class TenantPortalRepository(
    ReportingSqlConnectionFactory reportingFactory,
    TenantConfigurationCache configurationCache) : ITenantPortalRepository
{
    public async Task<TenantOverview?> GetOverviewAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var snapshot = await configurationCache.GetAsync(tenantId, cancellationToken);
        if (snapshot is null || !snapshot.Tenant.IsActive) return null;

        var providers = snapshot.Providers
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Provider, StringComparer.Ordinal)
            .Select(x => new AvailableProvider(x.Provider, x.FromNumber, x.IsDefault))
            .ToArray();

        using var reportingConnection = reportingFactory.CreateConnection();
        var counts = await reportingConnection.QuerySingleOrDefaultAsync<Counts>(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/TenantPortalRepository.GetOverviewCountsAsync.01.sql"),
            new { TenantId = tenantId },
            cancellationToken: cancellationToken)) ?? new Counts(0, 0, 0, 0, 0);

        return new(
            snapshot.Tenant.Name,
            snapshot.Tenant.TimeZoneId,
            counts.Outbound,
            counts.Inbound,
            counts.Delivered,
            counts.Failed,
            counts.Pending,
            providers);
    }

    private sealed record Counts(long Outbound, long Inbound, long Delivered, long Failed, long Pending);
}

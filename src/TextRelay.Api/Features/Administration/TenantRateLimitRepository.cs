using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Sms.Application.Administration;
using Sms.Infrastructure.Caching;

namespace Sms.Infrastructure.Persistence;

public sealed class TenantRateLimitRepository(
    SqlConnectionFactory connections,
    ResilientDistributedCache cache) : ITenantRateLimitRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TenantRateLimitSettings> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var key = CacheKey(tenantId);
        var cached = await cache.GetStringAsync(key, "RateLimit", tenantId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            var value = JsonSerializer.Deserialize<TenantRateLimitSettings>(cached, JsonOptions);
            if (value is not null) return value;
        }

        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/TenantRateLimitRepository.GetAsync.01.sql");
        using var connection = connections.CreateConnection();
        var settings = await connection.QuerySingleOrDefaultAsync<TenantRateLimitSettings>(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: cancellationToken))
            ?? new TenantRateLimitSettings(120, 10, 6);

        await cache.SetStringAsync(key, JsonSerializer.Serialize(settings, JsonOptions), "RateLimit", tenantId, cancellationToken);
        return settings;
    }

    public async Task SaveAsync(Guid tenantId, TenantRateLimitSettings settings, CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/TenantRateLimitRepository.SaveAsync.01.sql");
        using var connection = connections.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            TenantId = tenantId,
            settings.RequestsPerMinute,
            settings.SmsPerMinute,
            settings.OllamaRequestsPerMinute
        }, cancellationToken: cancellationToken));

        await cache.RemoveAsync(CacheKey(tenantId), "RateLimit", tenantId, CancellationToken.None);
    }

    private static string CacheKey(Guid tenantId) => $"tenant-config:rate-limit:{tenantId:N}";
}

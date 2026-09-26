using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Sms.Infrastructure.Caching;
using Sms.Application.Administration;
using Sms.Application.Auth;
using Sms.Application.Providers;

namespace Sms.Infrastructure.Persistence;

public sealed record TenantConfigurationSnapshot(
    TenantSummary Tenant,
    ClientSummary[] Clients,
    TenantSmsProviderConfiguration[] Providers);

public sealed class TenantConfigurationCache(
    SqlConnectionFactory connectionFactory,
    ResilientDistributedCache cache)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TenantConfigurationSnapshot?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var key = TenantKey(tenantId);
        var cached = await cache.GetStringAsync(key, "TenantConfiguration", tenantId: null, cancellationToken, key);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            var value = JsonSerializer.Deserialize<TenantConfigurationSnapshot>(cached, JsonOptions);
            if (value is not null) return value;
        }

        using var connection = connectionFactory.CreateConnection();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/TenantConfigurationCache.GetAsync.01.sql"),
            new { TenantId = tenantId },
            cancellationToken: cancellationToken));

        var tenant = await results.ReadSingleOrDefaultAsync<TenantSummary>();
        var clients = (await results.ReadAsync<ClientSummary>()).ToArray();
        var providers = (await results.ReadAsync<TenantSmsProviderConfiguration>()).ToArray();
        if (tenant is null) return null;

        var snapshot = new TenantConfigurationSnapshot(tenant, clients, providers);
        await cache.SetStringAsync(key, JsonSerializer.Serialize(snapshot, JsonOptions), "TenantConfiguration", tenantId: null, cancellationToken, key);
        return snapshot;
    }

    public async Task<ApiClientCredential?> GetApiClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var key = ApiClientKey(clientId);
        var cached = await cache.GetStringAsync(key, "TenantConfiguration", tenantId: null, cancellationToken, key);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            var value = JsonSerializer.Deserialize<ApiClientCredential>(cached, JsonOptions);
            if (value is not null) return value;
        }

        using var connection = connectionFactory.CreateConnection();
        var valueFromDatabase = await connection.QuerySingleOrDefaultAsync<ApiClientCredential>(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/ApiClientRepository.GetActiveByClientIdAsync.01.sql"),
            new { ClientId = clientId },
            cancellationToken: cancellationToken));

        if (valueFromDatabase is not null)
            await cache.SetStringAsync(key, JsonSerializer.Serialize(valueFromDatabase, JsonOptions), "TenantConfiguration", tenantId: null, cancellationToken, key);

        return valueFromDatabase;
    }

    public async Task<TenantSmsProviderConfiguration?> GetProviderByRouteAsync(
        string provider,
        string accountId,
        string number,
        CancellationToken cancellationToken = default)
    {
        var key = ProviderRouteKey(provider, accountId, number);
        var cached = await cache.GetStringAsync(key, "TenantConfiguration", tenantId: null, cancellationToken, key);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            var value = JsonSerializer.Deserialize<TenantSmsProviderConfiguration>(cached, JsonOptions);
            if (value is not null) return value;
        }

        using var connection = connectionFactory.CreateConnection();
        var valueFromDatabase = await connection.QuerySingleOrDefaultAsync<TenantSmsProviderConfiguration>(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/TenantSmsProviderRepository.GetByAccountAndNumberAsync.04.sql"),
            new { Provider = provider, AccountId = accountId, FromNumber = number.Trim() },
            cancellationToken: cancellationToken));

        if (valueFromDatabase is not null)
            await cache.SetStringAsync(key, JsonSerializer.Serialize(valueFromDatabase, JsonOptions), "TenantConfiguration", tenantId: null, cancellationToken, key);

        return valueFromDatabase;
    }

    public async Task InvalidateAsync(
        Guid tenantId,
        TenantConfigurationSnapshot? previous,
        TenantSmsProviderConfiguration? changedProvider = null,
        CancellationToken cancellationToken = default)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal)
        {
            TenantKey(tenantId)
        };

        if (previous is not null)
        {
            foreach (var client in previous.Clients)
                keys.Add(ApiClientKey(client.ClientId));

            foreach (var provider in previous.Providers)
                AddProviderRouteKey(keys, provider);
        }

        if (changedProvider is not null)
            AddProviderRouteKey(keys, changedProvider);

        foreach (var key in keys)
            await cache.RemoveAsync(key, "TenantConfiguration", tenantId, cancellationToken, key);
    }

    private static void AddProviderRouteKey(HashSet<string> keys, TenantSmsProviderConfiguration provider)
    {
        if (string.IsNullOrWhiteSpace(provider.AccountId) || string.IsNullOrWhiteSpace(provider.FromNumber))
            return;

        keys.Add(ProviderRouteKey(provider.Provider, provider.AccountId, provider.FromNumber));
    }

    private static string TenantKey(Guid tenantId) => $"tenant-config:{tenantId:N}";

    private static string ApiClientKey(string clientId) =>
        $"tenant-config:api-client:{Hash(clientId.Trim().ToUpperInvariant())}";

    private static string ProviderRouteKey(string provider, string accountId, string number) =>
        $"tenant-config:provider-route:{Hash($"{provider.Trim().ToUpperInvariant()}\0{accountId.Trim().ToUpperInvariant()}\0{number.Trim()}")}";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sms.Application.Administration;
using Sms.Application.Auth;
using Sms.Application.Providers;
using Sms.Infrastructure.Persistence;
using Sms.Infrastructure.Caching;

namespace Sms.Infrastructure.Tests;

public sealed class TenantConfigurationCacheTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetAsync_ReturnsCachedTenantConfiguration()
    {
        var tenantId = Guid.NewGuid();
        var snapshot = new TenantConfigurationSnapshot(
            new TenantSummary(tenantId, "Tenant", "UTC", true, DateTimeOffset.UtcNow),
            [new ClientSummary(Guid.NewGuid(), "client-one", true, DateTimeOffset.UtcNow)],
            [new TenantSmsProviderConfiguration(tenantId, "Twilio", "account", "encrypted-secret", "+15550000001", true, true)]);

        var distributed = new RecordingDistributedCache();
        await distributed.SetStringAsync($"tenant-config:{tenantId:N}", JsonSerializer.Serialize(snapshot, JsonOptions));
        var cache = Create(distributed);

        var result = await cache.GetAsync(tenantId);

        Assert.NotNull(result);
        Assert.Equal("UTC", result!.Tenant.TimeZoneId);
        Assert.Single(result.Clients);
        Assert.Single(result.Providers);
    }

    [Fact]
    public async Task GetApiClientAsync_ReturnsCachedCredential()
    {
        var credential = new ApiClientCredential(
            Guid.NewGuid(),
            "client-one",
            RandomNumberGenerator.GetBytes(32),
            RandomNumberGenerator.GetBytes(32),
            600000);
        var distributed = new RecordingDistributedCache();
        await distributed.SetStringAsync(ApiClientKey(credential.ClientId), JsonSerializer.Serialize(credential, JsonOptions));
        var cache = Create(distributed);

        var result = await cache.GetApiClientAsync("CLIENT-ONE");

        Assert.NotNull(result);
        Assert.Equal(credential.TenantId, result!.TenantId);
        Assert.Equal(credential.SecretHash, result.SecretHash);
    }

    [Fact]
    public async Task GetProviderByRouteAsync_ReturnsCachedEncryptedProvider()
    {
        var provider = new TenantSmsProviderConfiguration(
            Guid.NewGuid(),
            "Bandwidth",
            "account",
            "encrypted-secret",
            "+15550000002",
            true,
            true,
            "encrypted-settings");
        var distributed = new RecordingDistributedCache();
        await distributed.SetStringAsync(
            ProviderRouteKey(provider.Provider, provider.AccountId, provider.FromNumber!),
            JsonSerializer.Serialize(provider, JsonOptions));
        var cache = Create(distributed);

        var result = await cache.GetProviderByRouteAsync("bandwidth", "ACCOUNT", " +15550000002 ");

        Assert.NotNull(result);
        Assert.Equal("encrypted-secret", result!.ApiSecret);
        Assert.Equal("encrypted-settings", result.Settings);
    }

    [Fact]
    public async Task InvalidateAsync_RemovesSnapshotClientAndProviderKeys()
    {
        var tenantId = Guid.NewGuid();
        var previous = new TenantConfigurationSnapshot(
            new TenantSummary(tenantId, "Tenant", "UTC", true, DateTimeOffset.UtcNow),
            [
                new ClientSummary(Guid.NewGuid(), "client-one", true, DateTimeOffset.UtcNow),
                new ClientSummary(Guid.NewGuid(), "client-two", true, DateTimeOffset.UtcNow)
            ],
            [
                new TenantSmsProviderConfiguration(tenantId, "Twilio", "account-a", "secret", "+15550000001", true, true),
                new TenantSmsProviderConfiguration(tenantId, "Bandwidth", "account-b", "secret", "+15550000002", false, true)
            ]);
        var changed = new TenantSmsProviderConfiguration(
            tenantId,
            "Twilio",
            "account-new",
            "secret",
            "+15550000003",
            true,
            true);

        var distributed = new RecordingDistributedCache();
        var cache = Create(distributed);

        await cache.InvalidateAsync(tenantId, previous, changed);

        Assert.Equal(6, distributed.RemovedKeys.Count);
        Assert.Equal(distributed.RemovedKeys.Count, distributed.RemovedKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains($"tenant-config:{tenantId:N}", distributed.RemovedKeys);
    }

    [Fact]
    public async Task InvalidateAsync_DoesNotTouchDistributedCacheWhenCacheIsDisabled()
    {
        var tenantId = Guid.NewGuid();
        var previous = new TenantConfigurationSnapshot(
            new TenantSummary(tenantId, "Tenant", "UTC", true, DateTimeOffset.UtcNow),
            [new ClientSummary(Guid.NewGuid(), "client-one", true, DateTimeOffset.UtcNow)],
            [new TenantSmsProviderConfiguration(tenantId, "Twilio", "account", "secret", "+15550000001", true, true)]);

        var cache = Create(new DisabledDistributedCacheProxy());

        await cache.InvalidateAsync(tenantId, previous);
    }

    private static TenantConfigurationCache Create(IDistributedCache cache)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=invalid;Database=invalid;Username=invalid;Password=invalid"
            })
            .Build();

        return new TenantConfigurationCache(
            new SqlConnectionFactory(configuration),
            new ResilientDistributedCache(cache, NullLogger<ResilientDistributedCache>.Instance));
    }

    private static string ApiClientKey(string clientId) =>
        $"tenant-config:api-client:{Hash(clientId.Trim().ToUpperInvariant())}";

    private static string ProviderRouteKey(string provider, string accountId, string number) =>
        $"tenant-config:provider-route:{Hash($"{provider.Trim().ToUpperInvariant()}\0{accountId.Trim().ToUpperInvariant()}\0{number.Trim()}")}";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);
        public List<string> RemovedKeys { get; } = [];

        public byte[]? Get(string key) => _values.GetValueOrDefault(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromResult(Get(key));

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key)
        {
            RemovedKeys.Add(key);
            _values.Remove(key);
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            _values[key] = value;

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }
}

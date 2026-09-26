using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Sms.Application.Administration;
using Sms.Infrastructure.Caching;
using Sms.Infrastructure.Providers;

namespace Sms.Infrastructure.Tests;

public sealed class ProviderCatalogCacheTests
{
    [Fact]
    public async Task GetAsync_CachesProviderDefinitionsInDistributedCache()
    {
        var distributedCache = new TenantConfigurationCacheTestFactory.TestDistributedCache();
        var policies = new CountingPolicies();
        var cache = new ProviderCatalogCache(
            Wrap(distributedCache),
            policies);

        var first = await cache.GetAsync();
        var second = await cache.GetAsync();

        Assert.Equal(2, first.Count);
        Assert.Equal(first.Count, second.Count);
        for (var index = 0; index < first.Count; index++)
        {
            Assert.Equal(first[index].Name, second[index].Name);
            Assert.Equal(first[index].AccountLabel, second[index].AccountLabel);
            Assert.Equal(first[index].SecretLabel, second[index].SecretLabel);
            Assert.Equal(first[index].Fields.ToArray(), second[index].Fields.ToArray());
        }
        Assert.Equal(1, policies.EnumerationCount);
    }

    [Fact]
    public async Task GetAsync_WhenCacheIsDisabled_ReadsPoliciesEveryTime()
    {
        var policies = new CountingPolicies();
        var cache = new ProviderCatalogCache(
            Wrap(new DisabledDistributedCacheProxy()),
            policies);

        await cache.GetAsync();
        await cache.GetAsync();

        Assert.Equal(2, policies.EnumerationCount);
    }


    [Fact]
    public async Task GetAsync_WhenCachedJsonIsInvalid_FallsBackToPolicies()
    {
        var distributedCache = new TenantConfigurationCacheTestFactory.TestDistributedCache();
        await distributedCache.SetStringAsync("provider-catalog", "{invalid-json");
        var policies = new CountingPolicies();
        var cache = new ProviderCatalogCache(
            Wrap(distributedCache),
            policies);

        var result = await cache.GetAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal(1, policies.EnumerationCount);
    }

    [Fact]
    public async Task GetAsync_WhenCancellationIsRequested_PropagatesCancellation()
    {
        var policies = new CountingPolicies();
        var cache = new ProviderCatalogCache(
            Wrap(new CancellingDistributedCache()),
            policies);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetAsync(cancellation.Token));

        Assert.Equal(0, policies.EnumerationCount);
    }

    [Fact]
    public async Task GetAsync_WhenCacheWriteFails_ReturnsPolicyCatalog()
    {
        var policies = new CountingPolicies();
        var cache = new ProviderCatalogCache(
            Wrap(new WriteFailingDistributedCache()),
            policies);

        var result = await cache.GetAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal(1, policies.EnumerationCount);
    }


    private static ResilientDistributedCache Wrap(IDistributedCache cache) =>
        new(cache, NullLogger<ResilientDistributedCache>.Instance);

    private sealed class CancellingDistributedCache : Microsoft.Extensions.Caching.Distributed.IDistributedCache
    {
        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromCanceled<byte[]?>(token);
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed class WriteFailingDistributedCache : Microsoft.Extensions.Caching.Distributed.IDistributedCache
    {
        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions options) => throw new InvalidOperationException("cache unavailable");
        public Task SetAsync(string key, byte[] value, Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions options, CancellationToken token = default) => throw new InvalidOperationException("cache unavailable");
    }

    private sealed class CountingPolicies : IEnumerable<IProviderSettingsPolicy>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<IProviderSettingsPolicy> GetEnumerator()
        {
            EnumerationCount++;
            return new IProviderSettingsPolicy[]
            {
                new TwilioSettingsPolicy(),
                new BandwidthSettingsPolicy()
            }.AsEnumerable().GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

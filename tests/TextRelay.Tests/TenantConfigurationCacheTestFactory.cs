using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Sms.Infrastructure.Persistence;
using Sms.Infrastructure.Caching;

namespace Sms.Infrastructure.Tests;

internal static class TenantConfigurationCacheTestFactory
{
    public static TenantConfigurationCache Create(SqlConnectionFactory connectionFactory) =>
        new(connectionFactory, new ResilientDistributedCache(new TestDistributedCache(), NullLogger<ResilientDistributedCache>.Instance));

    internal sealed class TestDistributedCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public byte[]? Get(string key) => _values.GetValueOrDefault(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromResult(Get(key));

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _values.TryRemove(key, out _);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            _values[key] = value;

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }
}

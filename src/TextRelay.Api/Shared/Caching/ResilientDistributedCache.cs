using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Sms.Infrastructure.Caching;

public sealed class ResilientDistributedCache(IDistributedCache cache, ILogger<ResilientDistributedCache> logger)
{
    public async Task<string?> GetStringAsync(string key, string area, Guid? tenantId, CancellationToken cancellationToken, string? diagnosticKey = null)
    {
        try { return await cache.GetStringAsync(key, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to read {CacheArea} cache for tenant {TenantId}. Key {CacheKey}.", area, tenantId, diagnosticKey);
            return null;
        }
    }

    public async Task SetStringAsync(string key, string value, string area, Guid? tenantId, CancellationToken cancellationToken, string? diagnosticKey = null)
    {
        try { await cache.SetStringAsync(key, value, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { logger.LogError(exception, "Failed to write {CacheArea} cache for tenant {TenantId}. Key {CacheKey}.", area, tenantId, diagnosticKey); }
    }

    public async Task RemoveAsync(string key, string area, Guid? tenantId, CancellationToken cancellationToken, string? diagnosticKey = null)
    {
        try { await cache.RemoveAsync(key, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { logger.LogError(exception, "Failed to invalidate {CacheArea} cache for tenant {TenantId}. Key {CacheKey}.", area, tenantId, diagnosticKey); }
    }
}

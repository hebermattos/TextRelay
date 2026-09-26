using System.Text.Json;
using Sms.Application.Administration;

namespace Sms.Infrastructure.Caching;

public sealed class ProviderCatalogCache(
    ResilientDistributedCache cache,
    IEnumerable<IProviderSettingsPolicy> policies) : IProviderCatalogCache
{
    private const string CacheKey = "provider-catalog";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ProviderDefinition>> GetAsync(CancellationToken cancellationToken = default)
    {
        var cached = await cache.GetStringAsync(CacheKey, "ProviderCatalog", null, cancellationToken);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            try
            {
                var definitions = JsonSerializer.Deserialize<ProviderDefinition[]>(cached, JsonOptions);
                if (definitions is not null) return definitions;
            }
            catch (JsonException)
            {
                // Treat invalid cached data as a cache miss and rebuild it from provider policies.
            }
        }

        var catalog = policies.Select(policy => policy.Definition).ToArray();
        await cache.SetStringAsync(
            CacheKey,
            JsonSerializer.Serialize(catalog, JsonOptions),
            "ProviderCatalog",
            null,
            cancellationToken);

        return catalog;
    }

}

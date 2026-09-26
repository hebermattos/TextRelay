using Microsoft.Extensions.Configuration;

namespace Sms.Infrastructure.Caching;

public static class CacheConfiguration
{
    public static bool IsEnabled(IConfiguration configuration)
    {
        var configured = configuration["Cache:Enabled"];
        return !bool.TryParse(configured, out var enabled) || enabled;
    }
}

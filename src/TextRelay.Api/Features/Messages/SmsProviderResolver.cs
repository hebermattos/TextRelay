using Microsoft.Extensions.Configuration;
using Sms.Application.Messages;

namespace Sms.Infrastructure.Providers;

public sealed class SmsProviderResolver(IEnumerable<ISmsProvider> providers, IConfiguration configuration) : ISmsProviderResolver
{
    public ISmsProvider Resolve(string? provider = null)
    {
        var name = provider ?? configuration["Sms:DefaultProvider"] ?? throw new InvalidOperationException("No default SMS provider is configured.");
        return providers.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"SMS provider '{name}' is not registered.");
    }
}

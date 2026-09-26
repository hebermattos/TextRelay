using Microsoft.Extensions.Configuration;
using Sms.Application.Messages;

namespace Sms.Infrastructure.Providers;

public sealed class ConfiguredSmsWebhookUrlProvider : ISmsWebhookUrlProvider
{
    private readonly string _publicBaseUrl;

    public ConfiguredSmsWebhookUrlProvider(IConfiguration configuration)
    {
        var value = configuration["Sms:PublicBaseUrl"]?.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("Sms:PublicBaseUrl must be an absolute HTTPS URL without credentials, query, or fragment.");
        }

        _publicBaseUrl = uri.ToString().TrimEnd('/');
    }

    public Uri GetUrl(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return new Uri($"{_publicBaseUrl}/{relativePath.TrimStart('/')}", UriKind.Absolute);
    }
}

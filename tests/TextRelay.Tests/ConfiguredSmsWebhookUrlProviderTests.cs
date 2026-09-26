using Microsoft.Extensions.Configuration;
using Sms.Infrastructure.Providers;

namespace Sms.Infrastructure.Tests;

public sealed class ConfiguredSmsWebhookUrlProviderTests
{
    [Fact]
    public void GetUrl_CombinesConfiguredBasePathAndRelativePath()
    {
        var provider = Create("https://sms.example.com/gateway/");

        var result = provider.GetUrl("/api/v1/webhooks/twilio/status?attempt=1");

        Assert.Equal("https://sms.example.com/gateway/api/v1/webhooks/twilio/status?attempt=1", result.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://sms.example.com")]
    [InlineData("https://user:password@sms.example.com")]
    [InlineData("https://sms.example.com?value=1")]
    [InlineData("https://sms.example.com#fragment")]
    public void Constructor_RejectsUnsafeOrMissingBaseUrl(string? value)
    {
        Assert.Throws<InvalidOperationException>(() => Create(value));
    }

    [Fact]
    public void GetUrl_RejectsBlankRelativePath()
    {
        Assert.Throws<ArgumentException>(() => Create("https://sms.example.com").GetUrl(" "));
    }

    private static ConfiguredSmsWebhookUrlProvider Create(string? value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sms:PublicBaseUrl"] = value })
            .Build();
        return new ConfiguredSmsWebhookUrlProvider(configuration);
    }
}

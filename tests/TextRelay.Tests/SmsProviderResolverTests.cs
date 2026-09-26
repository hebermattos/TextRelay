using Microsoft.Extensions.Configuration;
using Sms.Application.Messages;
using Sms.Infrastructure.Providers;
using Sms.Domain.Messages;

namespace Sms.Infrastructure.Tests;

public sealed class SmsProviderResolverTests
{
    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var resolver = Create("Twilio", new FakeProvider("Twilio"));
        Assert.Equal("Twilio", resolver.Resolve("tWiLiO").Name);
    }

    [Fact]
    public void Resolve_UsesConfiguredDefault()
    {
        var resolver = Create("Twilio", new FakeProvider("Twilio"));
        Assert.Equal("Twilio", resolver.Resolve().Name);
    }

    [Fact]
    public void Resolve_ThrowsWhenProviderIsMissing()
    {
        var resolver = Create("Twilio", new FakeProvider("Twilio"));
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve("Unknown"));
    }

    [Fact]
    public void Resolve_ThrowsWithoutDefault()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var resolver = new SmsProviderResolver([new FakeProvider("Twilio")], configuration);
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve());
    }

    private static SmsProviderResolver Create(string defaultProvider, params ISmsProvider[] providers)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Sms:DefaultProvider"] = defaultProvider }).Build();
        return new SmsProviderResolver(providers, configuration);
    }

    private sealed class FakeProvider(string name) : ISmsProvider
    {
        public string Name { get; } = name;
        public Task<ProviderSendResult> SendAsync(string from, string to, string body, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderSendResult("id", SmsStatus.Sent));
    }
}

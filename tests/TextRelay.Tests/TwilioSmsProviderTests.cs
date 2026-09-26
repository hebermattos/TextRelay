using System.Net;
using Microsoft.Extensions.Configuration;
using Sms.Application.Common;
using Sms.Application.Providers;
using Sms.Infrastructure.Providers;
using Sms.Domain.Messages;

namespace Sms.Infrastructure.Tests;

public sealed class TwilioSmsProviderTests
{
    [Fact]
    public async Task SendAsync_UsesTenantConfigurationAndDefaultFrom()
    {
        var tenantId = Guid.NewGuid();
        var handler = new RecordingHandler(HttpStatusCode.Created, """{"sid":"SM123","status":"queued"}""");
        var provider = Create(tenantId, handler, Configuration(tenantId, from: "+15550000001"));

        var result = await provider.SendAsync("", "+15550000002", "hello");

        Assert.Equal("SM123", result.ProviderMessageId);
        Assert.Equal(SmsStatus.Pending, result.Status);
        Assert.Contains("Accounts/AC123/Messages.json", handler.RequestUri);
        Assert.Contains("From=%2B15550000001", handler.Body);
        Assert.Contains("To=%2B15550000002", handler.Body);
        Assert.Contains("StatusCallback=https%3A%2F%2Fsms.example.com%2Fapi%2Fv1%2Fwebhooks%2Ftwilio%2Fstatus", handler.Body);
        Assert.Equal("Basic", handler.AuthorizationScheme);
    }

    [Fact]
    public async Task SendAsync_AllowsConfiguredExplicitFrom()
    {
        var tenantId = Guid.NewGuid();
        var handler = new RecordingHandler(HttpStatusCode.Created, """{"sid":"SM1","status":"sent"}""");
        var provider = Create(tenantId, handler, Configuration(tenantId, from: "+100"));
        await provider.SendAsync("+100", "+300", "body");
        Assert.Contains("From=%2B100", handler.Body);
    }

    [Fact]
    public async Task SendAsync_RejectsFromNumberOwnedByAnotherTenantRoute()
    {
        var tenantId = Guid.NewGuid();
        var provider = Create(tenantId, new RecordingHandler(HttpStatusCode.Created, "{}"), Configuration(tenantId, from: "+100"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SendAsync("+200", "+300", "body"));

        Assert.Contains("not configured", error.Message);
    }

    [Fact]
    public async Task SendAsync_RejectsMissingConfiguration()
    {
        var tenantId = Guid.NewGuid();
        var provider = Create(tenantId, new RecordingHandler(HttpStatusCode.OK, "{}"), null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SendAsync("", "+1", "body"));
    }

    [Fact]
    public async Task SendAsync_RejectsMissingFrom()
    {
        var tenantId = Guid.NewGuid();
        var provider = Create(tenantId, new RecordingHandler(HttpStatusCode.OK, "{}"), Configuration(tenantId, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SendAsync("", "+1", "body"));
    }

    [Fact]
    public async Task SendAsync_ThrowsOnProviderFailure()
    {
        var tenantId = Guid.NewGuid();
        var provider = Create(tenantId, new RecordingHandler(HttpStatusCode.Unauthorized, """{"message":"bad"}"""), Configuration(tenantId, "+1"));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.SendAsync("", "+2", "body"));
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task SendAsync_RejectsResponseWithoutSid()
    {
        var tenantId = Guid.NewGuid();
        var provider = Create(tenantId, new RecordingHandler(HttpStatusCode.Created, """{"status":"queued"}"""), Configuration(tenantId, "+1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SendAsync("", "+2", "body"));
    }

    private static TwilioSmsProvider Create(Guid tenantId, HttpMessageHandler handler, TenantSmsProviderConfiguration? config)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.twilio.com/") };
        return new TwilioSmsProvider(client, new TenantContext(tenantId), new ProviderRepository(config), WebhookUrls());
    }

    private static ConfiguredSmsWebhookUrlProvider WebhookUrls() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Sms:PublicBaseUrl"] = "https://sms.example.com" })
        .Build());

    private static TenantSmsProviderConfiguration Configuration(Guid tenantId, string? from) =>
        new(tenantId, "Twilio", "AC123", "secret", from, true, true);

    private sealed record TenantContext(Guid TenantId) : ITenantContext;

    private sealed class ProviderRepository(TenantSmsProviderConfiguration? configuration) : ITenantSmsProviderRepository
    {
        public Task<TenantSmsProviderConfiguration?> GetAsync(Guid tenantId, string provider, CancellationToken cancellationToken = default) => Task.FromResult(configuration);
        public Task<TenantSmsProviderConfiguration?> GetDefaultAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(configuration);
        public Task<TenantSmsProviderConfiguration?> GetByAccountAndNumberAsync(string provider, string accountId, string number, CancellationToken cancellationToken = default) => Task.FromResult(configuration);
        public Task UpsertAsync(TenantSmsProviderConfiguration configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public string RequestUri { get; private set; } = "";
        public string Body { get; private set; } = "";
        public string? AuthorizationScheme { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString() ?? "";
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            return new HttpResponseMessage(status) { Content = new StringContent(response) };
        }
    }
}

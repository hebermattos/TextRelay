using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Controllers;
using Sms.Application.Messages;
using Sms.Application.Providers;
using Sms.Domain.Messages;
using Sms.Infrastructure.Providers;

namespace Sms.Infrastructure.Tests;

public sealed class BandwidthWebhooksControllerTests
{
    [Fact]
    public async Task Inbound_UsesAuthenticatedOwnerAndPreservesProviderTime()
    {
        var fixture = new Fixture();
        var payload = Payload();
        payload[0]!["tenantId"] = Guid.NewGuid().ToString();
        payload[0]!["message"]!["to"] = new JsonArray("+19999999999", "+15550000001");

        Assert.IsType<NoContentResult>(await fixture.Controller(payload).Inbound(default));

        var message = Assert.Single(fixture.Messages.Inbound);
        Assert.Equal(fixture.TenantId, message.TenantId);
        Assert.Equal("Bandwidth", message.Provider);
        Assert.Equal("message-1", message.ProviderMessageId);
        Assert.Equal("+15550000001", message.To);
        Assert.Equal("+15550000002", message.From);
        Assert.Equal("test body", message.Body);
        Assert.Equal(SmsDirection.Inbound, message.Direction);
        Assert.Equal(SmsStatus.Received, message.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-09-17T12:00:00Z"), message.CreatedAt);
    }

    [Theory]
    [InlineData("message-sending", SmsStatus.Sent)]
    [InlineData("message-sent", SmsStatus.Sent)]
    [InlineData("message-delivered", SmsStatus.Delivered)]
    [InlineData("message-failed", SmsStatus.Failed)]
    public async Task Status_MapsEventsAndScopesUpdates(string type, SmsStatus expected)
    {
        var fixture = new Fixture();
        Assert.IsType<NoContentResult>(await fixture.Controller(Payload(type)).Status(default));
        var update = Assert.Single(fixture.Messages.Updates);
        Assert.Equal(fixture.TenantId, update.TenantId);
        Assert.Equal("Bandwidth", update.Provider);
        Assert.Equal("message-1", update.MessageId);
        Assert.Equal(expected, update.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-09-17T12:00:00Z"), update.Time);
        Assert.Empty(fixture.Messages.Inbound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer token")]
    [InlineData("Basic")]
    [InlineData("Basic !!!")]
    [InlineData("Basic /w==")]
    [InlineData("Basic bm9jb2xvbg==")]
    [InlineData("Basic OnBhc3M=")]
    [InlineData("Basic dXNlcjo=")]
    public async Task Inbound_ChallengesMissingOrMalformedCredentials(string? authorization)
    {
        var fixture = new Fixture();
        var controller = fixture.Controller(Payload(), authorization);
        Assert.IsType<UnauthorizedResult>(await controller.Inbound(default));
        Assert.StartsWith("Basic ", controller.Response.Headers.WWWAuthenticate.ToString());
        Assert.Empty(fixture.Messages.Inbound);
        Assert.Equal(0, fixture.Providers.Lookups);
    }

    [Fact]
    public async Task MissingCredentials_ChallengesEvenWithoutBodyOrContentType()
    {
        var fixture = new Fixture();
        var controller = fixture.Controller(Payload(), null);
        controller.Request.ContentType = null;
        controller.Request.Body = Stream.Null;
        Assert.IsType<UnauthorizedResult>(await controller.Inbound(default));
    }

    [Theory]
    [InlineData("wrong-client", "callback-password")]
    [InlineData("client-1", "wrong-password")]
    [InlineData("client-1", "oauth-secret")]
    public async Task Inbound_RejectsUnknownAccountAndWrongSecrets(string username, string password)
    {
        var fixture = new Fixture();
        Assert.IsType<UnauthorizedResult>(await fixture.Controller(Payload(), Basic(username, password)).Inbound(default));
        Assert.Empty(fixture.Messages.Inbound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"applicationId\":\"app-1\",\"webhookPassword\":\"\"}")]
    [InlineData("{\"applicationId\":\"other-app\",\"webhookPassword\":\"callback-password\"}")]
    public async Task Inbound_FailsClosedForMissingOrInvalidSettings(string? settings)
    {
        var fixture = new Fixture();
        fixture.Providers.Configurations[0] = fixture.Providers.Configurations[0] with { Settings = settings };
        Assert.IsType<UnauthorizedResult>(await fixture.Controller(Payload()).Inbound(default));
        Assert.Empty(fixture.Messages.Inbound);
    }

    [Fact]
    public async Task Inbound_RejectsInactiveConfiguration()
    {
        var fixture = new Fixture();
        fixture.Providers.Configurations[0] = fixture.Providers.Configurations[0] with { IsActive = false };
        Assert.IsType<UnauthorizedResult>(await fixture.Controller(Payload()).Inbound(default));
        Assert.Empty(fixture.Messages.Inbound);
    }

    [Fact]
    public async Task Inbound_RejectsOtherApplication()
    {
        var fixture = new Fixture();
        var payload = Payload();
        payload[0]!["message"]!["applicationId"] = "other-app";
        Assert.IsType<UnauthorizedResult>(await fixture.Controller(payload).Inbound(default));
        Assert.Empty(fixture.Messages.Inbound);
    }

    [Fact]
    public async Task Inbound_AuthenticatesEveryBatchMemberBeforeWriting()
    {
        var fixture = new Fixture();
        fixture.Providers.Configurations.Add(new(Guid.NewGuid(), "Bandwidth", "client-1", "oauth-secret",
            "+15550000003", false, true, "{\"applicationId\":\"app-1\",\"webhookPassword\":\"other-password\"}"));
        var payload = Payload();
        var other = payload[0]!.DeepClone();
        other["to"] = "+15550000003";
        other["message"]!["owner"] = "+15550000003";
        other["message"]!["to"] = new JsonArray("+15550000003");
        payload.Add(other);

        Assert.IsType<UnauthorizedResult>(await fixture.Controller(payload).Inbound(default));
        Assert.Empty(fixture.Messages.Inbound);
        Assert.Empty(fixture.Messages.Updates);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("not json")]
    public async Task Inbound_RejectsInvalidJsonAndEmptyBatches(string json)
    {
        var fixture = new Fixture();
        var controller = fixture.Controller(Payload());
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.IsType<BadRequestResult>(await controller.Inbound(default));
        Assert.Empty(fixture.Messages.Inbound);
    }

    [Theory]
    [InlineData("message", null)]
    [InlineData("time", null)]
    [InlineData("time", "not-a-time")]
    [InlineData("type", "unknown")]
    [InlineData("to", "+15550000009")]
    [InlineData("message.id", "")]
    [InlineData("message.from", null)]
    [InlineData("message.owner", "+15550000009")]
    [InlineData("message.applicationId", null)]
    [InlineData("message.direction", "out")]
    [InlineData("message.to", null)]
    public async Task Inbound_ValidatesWholeBatchBeforeWriting(string path, string? value)
    {
        var fixture = new Fixture();
        var payload = Payload();
        var invalid = payload[0]!.DeepClone();
        var parts = path.Split('.');
        var parent = parts.Length == 1 ? invalid : invalid[parts[0]]!;
        parent[parts[^1]] = value;
        payload.Add(invalid);
        Assert.IsType<BadRequestResult>(await fixture.Controller(payload).Inbound(default));
        Assert.Empty(fixture.Messages.Inbound);
    }

    [Fact]
    public async Task Inbound_RejectsOversizedBatchAndCredentials()
    {
        var fixture = new Fixture();
        var payload = Payload();
        while (payload.Count <= 100) payload.Add(payload[0]!.DeepClone());
        Assert.IsType<BadRequestResult>(await fixture.Controller(payload).Inbound(default));
        Assert.IsType<UnauthorizedResult>(await fixture.Controller(Payload(), new string('x', 4097)).Inbound(default));
        Assert.Empty(fixture.Messages.Inbound);
    }

    [Fact]
    public async Task Inbound_RejectsFieldsLargerThanDatabaseColumns()
    {
        var fixture = new Fixture();
        var payload = Payload();
        payload[0]!["message"]!["id"] = new string('x', 201);
        Assert.IsType<BadRequestResult>(await fixture.Controller(payload).Inbound(default));
        Assert.Empty(fixture.Messages.Inbound);
    }

    [Fact]
    public async Task Inbound_AcceptsEmptyTextAndPasswordContainingColon()
    {
        var fixture = new Fixture();
        fixture.Providers.Configurations[0] = fixture.Providers.Configurations[0] with
        { Settings = "{\"applicationId\":\"app-1\",\"webhookPassword\":\"pass:word\"}" };
        var payload = Payload();
        payload[0]!["message"]!.AsObject().Remove("text");
        Assert.IsType<NoContentResult>(await fixture.Controller(payload, Basic("client-1", "pass:word")).Inbound(default));
        Assert.Equal(string.Empty, Assert.Single(fixture.Messages.Inbound).Body);
    }

    [Fact]
    public async Task Status_RejectsWrongDirectionOrOwner()
    {
        var fixture = new Fixture();
        Assert.IsType<BadRequestResult>(await fixture.Controller(Payload()).Status(default));
        var payload = Payload("message-delivered");
        payload[0]!["message"]!["owner"] = "+15550000009";
        Assert.IsType<BadRequestResult>(await fixture.Controller(payload).Status(default));
        Assert.Empty(fixture.Messages.Updates);
    }

    [Fact]
    public async Task Inbound_RejectsUnsupportedContentType()
    {
        var fixture = new Fixture();
        var controller = fixture.Controller(Payload());
        controller.Request.ContentType = "text/plain";
        var result = Assert.IsType<StatusCodeResult>(await controller.Inbound(default));
        Assert.Equal(415, result.StatusCode);
        Assert.Empty(fixture.Messages.Inbound);
    }

    [Fact]
    public async Task Receive_PropagatesCancellationAndStorageFailures()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        await fixture.Controller(Payload()).Inbound(token);
        Assert.Equal(token, fixture.Providers.LastToken);
        Assert.Equal(token, fixture.Messages.LastToken);
        fixture.Messages.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Controller(Payload()).Inbound(token));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Controller(Payload()).Inbound(token));
    }

    internal static string Basic(string username = "client-1", string password = "callback-password") =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

    internal static JsonArray Payload(string type = "message-received")
    {
        var inbound = type == "message-received";
        return new JsonArray(new JsonObject
        {
            ["type"] = type, ["time"] = "2026-09-17T12:00:00Z",
            ["to"] = inbound ? "+15550000001" : "+15550000002",
            ["message"] = new JsonObject
            {
                ["id"] = "message-1", ["owner"] = "+15550000001", ["applicationId"] = "app-1",
                ["direction"] = inbound ? "in" : "out", ["from"] = inbound ? "+15550000002" : "+15550000001",
                ["to"] = new JsonArray(inbound ? "+15550000001" : "+15550000002"), ["text"] = "test body"
            }
        });
    }

    private sealed class Fixture
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public MessageRepository Messages { get; } = new();
        public ProviderRepository Providers { get; } = new();

        public Fixture() => Providers.Configurations.Add(new(TenantId, "Bandwidth", "client-1", "oauth-secret",
            "+15550000001", true, true, "{\"applicationId\":\"app-1\",\"webhookPassword\":\"callback-password\"}"));

        public BandwidthWebhooksController Controller(JsonArray payload) => Controller(payload, Basic());

        public BandwidthWebhooksController Controller(JsonArray payload, string? authorization)
        {
            var controller = new BandwidthWebhooksController(new ReceiveSmsWebhookService(Messages, new(new TestOptOutRepository())), new BandwidthWebhookParser(Providers));
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            controller.Request.ContentType = "application/json";
            controller.Request.Headers.Authorization = authorization;
            controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload.ToJsonString()));
            return controller;
        }
    }

    private sealed class ProviderRepository : ITenantSmsProviderRepository
    {
        public List<TenantSmsProviderConfiguration> Configurations { get; } = [];
        public int Lookups { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public Task<TenantSmsProviderConfiguration?> GetByAccountAndNumberAsync(string provider, string account, string number, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Lookups++;
            LastToken = cancellationToken;
            return Task.FromResult(Configurations.SingleOrDefault(x => x.Provider == provider && x.AccountId == account && x.FromNumber == number));
        }
        public Task<TenantSmsProviderConfiguration?> GetAsync(Guid t, string p, CancellationToken c = default) => throw new NotSupportedException();
        public Task<TenantSmsProviderConfiguration?> GetDefaultAsync(Guid t, CancellationToken c = default) => throw new NotSupportedException();
        public Task UpsertAsync(TenantSmsProviderConfiguration value, CancellationToken c = default) => throw new NotSupportedException();
    }

    private sealed class MessageRepository : ISmsMessageRepository
    {
        public List<SmsMessage> Inbound { get; } = [];
        public List<(Guid TenantId, string Provider, string MessageId, SmsStatus Status, DateTimeOffset Time)> Updates { get; } = [];
        public CancellationToken LastToken { get; private set; }
        public bool Fail { get; set; }
        public Task InsertInboundIfNotExistsAsync(SmsMessage message, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("Storage unavailable");
            LastToken = cancellationToken;
            Inbound.Add(message);
            return Task.CompletedTask;
        }
        public Task UpdateStatusByProviderMessageIdAsync(Guid tenant, string provider, string id, SmsStatus status, DateTimeOffset time, CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            Updates.Add((tenant, provider, id, status, time));
            return Task.CompletedTask;
        }
        public Task<SmsMessage?> GetByIdAsync(Guid t, Guid i, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SmsMessage>> GetHistoryAsync(Guid t, int s, int n, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SmsStatusHistory>> GetStatusHistoryAsync(Guid t, Guid i, CancellationToken c = default) => throw new NotSupportedException();
        public Task InsertAsync(SmsMessage m, CancellationToken c = default) => throw new NotSupportedException();
        public Task<bool> TryQueueScheduledAsync(Guid t, Guid i, DateTimeOffset u, CancellationToken c = default) => throw new NotSupportedException();
        public Task<bool> TryClaimQueuedAsync(Guid t,Guid i,DateTimeOffset u,CancellationToken c=default)=>Task.FromResult(false);
        public Task UpdateQueueStatusAsync(Guid t,Guid i,SmsQueueStatus s,DateTimeOffset u,CancellationToken c=default)=>Task.CompletedTask;
        public Task UpdateStatusAsync(Guid t, Guid i, SmsStatus s, string? p, DateTimeOffset u, CancellationToken c = default) => throw new NotSupportedException();
    }
}

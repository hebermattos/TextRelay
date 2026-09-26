using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Sms.Application.Messages;
using Sms.Application.Providers;
using Sms.Domain.Messages;
using Sms.Infrastructure.Persistence;
using Sms.Infrastructure.Providers;
using Sms.Infrastructure.Security;

namespace Sms.Infrastructure.Tests;

[Collection(PostgresTestCollection.Name)]
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMS_TEST_POSTGRES")))
            Skip = "Set SMS_TEST_POSTGRES to a PostgreSQL test database initialized with database/schema.sql.";
    }
}

public sealed class BandwidthWebhookSqlTests
{
    [PostgresFact]
    public async Task Callbacks_PersistEncryptedSettingsAndIsolateIdempotentHistory()
    {
        var connectionString = Environment.GetEnvironmentVariable("SMS_TEST_POSTGRES")!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
            ["Encryption:MasterKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }).Build();
        var factory = new SqlConnectionFactory(configuration);
        var configurationCache = TenantConfigurationCacheTestFactory.Create(factory);
        var providers = new TenantSmsProviderRepository(factory, new AesGcmSecretProtector(configuration), configurationCache);
        var contentProtector = new AesGcmSmsContentProtector(configuration);
        var messages = new SmsMessageRepository(factory, contentProtector);
        var service = new ReceiveSmsWebhookService(messages, new(new TestOptOutRepository()));
        var parser = new BandwidthWebhookParser(providers);
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var account = Guid.NewGuid().ToString("N");
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        try
        {
            await connection.ExecuteAsync("""
                INSERT INTO Tenants (Id, Name, IsActive, CreatedAt)
                VALUES (@Tenant, 'Webhook test', TRUE, CURRENT_TIMESTAMP),
                       (@OtherTenant, 'Other webhook test', TRUE, CURRENT_TIMESTAMP);
                """, new { Tenant = tenant, OtherTenant = otherTenant });
            var settings = "{\"accountId\":\"bandwidth-account\",\"applicationId\":\"app-1\",\"webhookPassword\":\"callback-password\"}";
            await providers.UpsertAsync(new(tenant, "Bandwidth", account, "oauth-secret", "+15550000001", true, true, settings));
            await providers.UpsertAsync(new(otherTenant, "Bandwidth", account, "other-oauth-secret", "+15550000003", true, true,
                settings.Replace("callback-password", "other-callback-password")));
            var stored = await connection.QuerySingleAsync<(string ApiSecret, string Settings)>(
                "SELECT ApiSecret, Settings FROM TenantSmsProviders WHERE TenantId=@Tenant AND Provider='Bandwidth';", new { Tenant = tenant });
            Assert.DoesNotContain("oauth-secret", stored.ApiSecret);
            Assert.DoesNotContain("callback-password", stored.Settings);
            Assert.DoesNotContain("applicationId", stored.Settings);
            Assert.Equal(settings, (await providers.GetAsync(tenant, "Bandwidth"))!.Settings);

            // Concurrent retries must create a single message and a single history entry.
            await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Receive(BandwidthWebhooksControllerTests.Payload(), SmsDirection.Inbound)));
            var inbound = Assert.Single(await messages.GetHistoryAsync(tenant, 0, 100));
            Assert.Equal(SmsStatus.Received, inbound.Status);
            Assert.Equal("test body", inbound.Body);
            var encrypted = await connection.QuerySingleAsync<(string From, string To, string Body)>(
                """SELECT "From", "To", Body FROM SmsMessages WHERE Id=@Id;""", new { inbound.Id });
            Assert.DoesNotContain(inbound.From, encrypted.From);
            Assert.DoesNotContain(inbound.To, encrypted.To);
            Assert.DoesNotContain(inbound.Body, encrypted.Body);
            Assert.Single(await messages.GetStatusHistoryAsync(tenant, inbound.Id));
            Assert.Empty(await messages.GetHistoryAsync(otherTenant, 0, 100));
            Assert.Null(await messages.GetByIdAsync(otherTenant, inbound.Id));
            Assert.Empty(await messages.GetStatusHistoryAsync(otherTenant, inbound.Id));

            var outbound = Outbound(tenant, "outbound-1");
            var other = Outbound(otherTenant, "outbound-1");
            await messages.InsertAsync(outbound);
            await messages.InsertAsync(other);
            foreach (var type in new[] { "message-sent", "message-sent", "message-delivered", "message-delivered", "message-sending", "message-failed" })
            {
                var payload = BandwidthWebhooksControllerTests.Payload(type);
                payload[0]!["message"]!["id"] = "outbound-1";
                await Receive(payload, SmsDirection.Outbound);
            }
            Assert.Equal(SmsStatus.Delivered, (await messages.GetByIdAsync(tenant, outbound.Id))!.Status);
            Assert.Equal(new[] { SmsStatus.Pending, SmsStatus.Sent, SmsStatus.Delivered },
                (await messages.GetStatusHistoryAsync(tenant, outbound.Id)).Select(x => x.Status).OrderBy(x => x));
            Assert.Equal(SmsStatus.Pending, (await messages.GetByIdAsync(otherTenant, other.Id))!.Status);
            Assert.Single(await messages.GetStatusHistoryAsync(otherTenant, other.Id));

            var scheduled = new SmsMessage
            {
                Id = Guid.NewGuid(), TenantId = tenant, Provider = "Bandwidth",
                From = "+15550000001", To = "+15550000002", Body = "scheduled test",
                Direction = SmsDirection.Outbound,
                QueueStatus = SmsQueueStatus.Scheduled,
                Status = SmsStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                ScheduledAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            };
            await messages.InsertAsync(scheduled);
            Assert.True(await messages.TryQueueScheduledAsync(tenant, scheduled.Id, DateTimeOffset.UtcNow));
            Assert.False(await messages.TryQueueScheduledAsync(tenant, scheduled.Id, DateTimeOffset.UtcNow));
            Assert.Single(await messages.GetStatusHistoryAsync(tenant, scheduled.Id));

            var failed = Outbound(tenant, "outbound-failed");
            await messages.InsertAsync(failed);
            var failure = BandwidthWebhooksControllerTests.Payload("message-failed");
            failure[0]!["message"]!["id"] = "outbound-failed";
            await Receive(failure, SmsDirection.Outbound);
            await Receive(failure, SmsDirection.Outbound);
            Assert.Equal(SmsStatus.Failed, (await messages.GetByIdAsync(tenant, failed.Id))!.Status);
            Assert.Equal(2, (await messages.GetStatusHistoryAsync(tenant, failed.Id)).Count);

            // Shared OAuth account does not allow one tenant's callback password to access another number.
            var forged = BandwidthWebhooksControllerTests.Payload();
            forged[0]!["to"] = "+15550000003";
            forged[0]!["message"]!["owner"] = "+15550000003";
            forged[0]!["message"]!["to"] = new JsonArray("+15550000003");
            using var forgedBody = new MemoryStream(Encoding.UTF8.GetBytes(forged.ToJsonString()));
            Assert.Equal(SmsWebhookResult.Unauthorized, await service.ReceiveAsync(parser,
                BandwidthWebhooksControllerTests.Basic(account), forgedBody, SmsDirection.Inbound));
            Assert.Single(await messages.GetHistoryAsync(otherTenant, 0, 100));

            async Task Receive(JsonArray payload, SmsDirection direction)
            {
                using var body = new MemoryStream(Encoding.UTF8.GetBytes(payload.ToJsonString()));
                Assert.Equal(SmsWebhookResult.Accepted, await service.ReceiveAsync(parser,
                    BandwidthWebhooksControllerTests.Basic(account), body, direction));
            }
        }
        finally
        {
            await connection.ExecuteAsync("""
                DELETE FROM SmsMessageStatusHistory WHERE TenantId IN (@Tenant, @OtherTenant);
                DELETE FROM SmsMessages WHERE TenantId IN (@Tenant, @OtherTenant);
                DELETE FROM TenantSmsProviders WHERE TenantId IN (@Tenant, @OtherTenant);
                DELETE FROM Tenants WHERE Id IN (@Tenant, @OtherTenant);
                """, new { Tenant = tenant, OtherTenant = otherTenant });
        }
    }

    private static SmsMessage Outbound(Guid tenant, string providerId) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant, Provider = "Bandwidth", ProviderMessageId = providerId,
        From = "+15550000001", To = "+15550000002", Body = "test", Direction = SmsDirection.Outbound,
        QueueStatus = SmsQueueStatus.Queued, Status = SmsStatus.Pending, CreatedAt = DateTimeOffset.Parse("2026-09-17T11:00:00Z")
    };
}

using System.Security.Cryptography;
using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sms.Application.Administration;
using Sms.Application.Auth;
using Sms.Application.Providers;
using Sms.Infrastructure.Persistence;
using Sms.Infrastructure.Caching;
using Sms.Infrastructure.Providers;
using Sms.Infrastructure.Security;

namespace Sms.Infrastructure.Tests;

[Collection(PostgresTestCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AdministrationSqlTests
{
    [ReportingSqlFact]
    public async Task Administration_PreservesIsolationSecretsAndSingleDefault()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = Environment.GetEnvironmentVariable("SMS_TEST_POSTGRES"),
            ["ConnectionStrings:ReportingPostgres"] = Environment.GetEnvironmentVariable("SMS_TEST_REPORTING_POSTGRES"),
            ["Encryption:MasterKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }).Build();
        var factory = new SqlConnectionFactory(configuration);
        var reportingFactory = new ReportingSqlConnectionFactory(configuration);
        var protector = new AesGcmSecretProtector(configuration);
        var configurationCache = TenantConfigurationCacheTestFactory.Create(factory);
        var repository = new AdministrationRepository(factory, protector, configurationCache);
        var providers = new TenantSmsProviderRepository(factory, protector, configurationCache);
        var service = new AdministrationService(repository, providers, [new TwilioSettingsPolicy(), new BandwidthSettingsPolicy()], new AdministrationServiceTests.TestProviderCatalogCache(), new TenantRateLimitRepository(factory, new ResilientDistributedCache(new TenantConfigurationCacheTestFactory.TestDistributedCache(), NullLogger<ResilientDistributedCache>.Instance)));
        var credentials = new ApiClientRepository(factory, configurationCache);
        var tenant = Guid.NewGuid(); var other = Guid.NewGuid(); var account = Guid.NewGuid().ToString("N");
        using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres"));
        using var reportingConnection = new NpgsqlConnection(configuration.GetConnectionString("ReportingPostgres"));
        await connection.OpenAsync();
        await reportingConnection.OpenAsync();
        try
        {
            await connection.ExecuteAsync("""
                INSERT INTO Tenants(Id, Name, IsActive, CreatedAt)
                VALUES (@Tenant, 'Portal test', TRUE, CURRENT_TIMESTAMP), (@Other, 'Other portal test', TRUE, CURRENT_TIMESTAMP);
                """, new { Tenant = tenant, Other = other });
            var issued = await service.CreateClientAsync(tenant, null, default);
            var stored = await credentials.GetActiveByClientIdAsync(issued.ClientId);
            Assert.NotNull(stored); Assert.True(ClientSecretHasher.Verify(issued.ClientSecret, stored.SecretHash, stored.SecretSalt, stored.SecretIterations));
            var client = Assert.Single(await service.ListClientsAsync(tenant, 0, 20, default));
            Assert.Empty(await service.ListClientsAsync(other, 0, 20, default));
            Assert.False(await repository.SetClientActiveAsync(other, client.Id, false, default));
            Assert.Null(await repository.RotateClientSecretAsync(other, client.Id, new byte[32], new byte[32], 100000, default));
            var rotated = await service.RotateClientSecretAsync(tenant, client.Id, default);
            stored = await credentials.GetActiveByClientIdAsync(issued.ClientId);
            Assert.False(ClientSecretHasher.Verify(issued.ClientSecret, stored!.SecretHash, stored.SecretSalt, stored.SecretIterations));
            Assert.True(ClientSecretHasher.Verify(rotated.ClientSecret, stored.SecretHash, stored.SecretSalt, stored.SecretIterations));
            await Assert.ThrowsAsync<AdministrationConflictException>(() => service.CreateClientAsync(other, issued.ClientId, default));
            await service.SetClientActiveAsync(tenant, client.Id, false, default);
            Assert.Null(await credentials.GetActiveByClientIdAsync(issued.ClientId));
            await service.SetClientActiveAsync(tenant, client.Id, true, default);
            await service.UpdateTenantAsync(tenant, "Renamed", "America/Sao_Paulo", false, default);
            Assert.Null(await credentials.GetActiveByClientIdAsync(issued.ClientId));
            await service.UpdateTenantAsync(tenant, "Renamed", "UTC", true, default);
            Assert.NotNull(await credentials.GetActiveByClientIdAsync(issued.ClientId));

            var twilio = new ProviderEdit(account, "+15550000001", true, true, "local-test-secret", null);
            var bandwidth = new ProviderEdit(account, "+15550000001", true, true, "local-bandwidth-secret",
                new() { ["accountId"] = "messaging", ["applicationId"] = "application", ["webhookPassword"] = "local-callback-password" });
            await service.SaveProviderAsync(tenant, "Twilio", twilio, default);
            await service.SaveProviderAsync(tenant, "Bandwidth", bandwidth, default);
            Assert.Equal("Bandwidth", (await providers.GetDefaultAsync(tenant))!.Provider);
            Assert.False((await providers.GetAsync(tenant, "Twilio"))!.IsDefault);
            Assert.Empty(await repository.ListProvidersAsync(other, default));
            var persisted = await connection.QuerySingleAsync<(string ApiSecret, string Settings)>(
                "SELECT ApiSecret, Settings FROM TenantSmsProviders WHERE TenantId=@Tenant AND Provider='Bandwidth';", new { Tenant = tenant });
            Assert.DoesNotContain("local-bandwidth-secret", persisted.ApiSecret);
            Assert.DoesNotContain("local-callback-password", persisted.Settings);
            await service.SaveProviderAsync(tenant, "Bandwidth", bandwidth with { ApiSecret = null, Settings = new() { ["webhookPassword"] = "" } }, default);
            Assert.Equal("local-bandwidth-secret", (await providers.GetAsync(tenant, "Bandwidth"))!.ApiSecret);
            await service.SaveProviderAsync(other, "Twilio", twilio with { FromNumber = "+15550000002" }, default);
            await Task.WhenAll(service.SaveProviderAsync(tenant, "Twilio", twilio, default), service.SaveProviderAsync(tenant, "Bandwidth", bandwidth, default));
            Assert.Single(await service.ListProvidersAsync(tenant, default), x => x.IsDefault);
            Assert.Equal("Twilio", (await providers.GetDefaultAsync(other))!.Provider);

            await reportingConnection.ExecuteAsync("""
                INSERT INTO TenantSmsOverview(TenantId, Outbound, Inbound, Delivered, Failed, Pending, UpdatedAtUtc)
                VALUES (@Tenant, 12, 3, 8, 1, 3, CURRENT_TIMESTAMP);
                """, new { Tenant = tenant });

            var overview = await new TenantPortalRepository(reportingFactory, configurationCache).GetOverviewAsync(tenant, default);
            Assert.Equal("Renamed", overview!.Name); Assert.Equal(12, overview.Outbound); Assert.Equal(3, overview.Inbound);
            Assert.Equal(8, overview.Delivered); Assert.Equal(1, overview.Failed); Assert.Equal(3, overview.Pending);
            Assert.Equal(2, overview.Providers.Count);
            Assert.Null(await new TenantPortalRepository(reportingFactory, configurationCache).GetOverviewAsync(Guid.NewGuid(), default));
        }
        finally
        {
            await reportingConnection.ExecuteAsync("DELETE FROM TenantSmsOverview WHERE TenantId IN (@Tenant, @Other);", new { Tenant = tenant, Other = other });
            await connection.ExecuteAsync("""
                DELETE FROM TenantSmsProviders WHERE TenantId IN (@Tenant, @Other);
                DELETE FROM ApiClients WHERE TenantId IN (@Tenant, @Other);
                DELETE FROM Tenants WHERE Id IN (@Tenant, @Other);
                """, new { Tenant = tenant, Other = other });
        }
    }
    [PostgresFact]
    public async Task Schema_RejectsInvalidTenantRelationships()
    {
        var connectionString = Environment.GetEnvironmentVariable("SMS_TEST_POSTGRES");
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var missingTenant = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        try
        {
            await connection.ExecuteAsync("""
                INSERT INTO Tenants(Id, Name, IsActive, CreatedAt)
                VALUES (@Tenant, 'Integrity tenant', TRUE, CURRENT_TIMESTAMP),
                       (@OtherTenant, 'Other integrity tenant', TRUE, CURRENT_TIMESTAMP);

                INSERT INTO SmsMessages
                    (Id, TenantId, "From", "To", Body, Provider, ProviderMessageId, Direction, QueueStatus, Status, CreatedAt)
                VALUES
                    (@MessageId, @Tenant, 'encrypted-from', 'encrypted-to', 'encrypted-body',
                     'Mock', NULL, 1, 2, 1, CURRENT_TIMESTAMP);
                """, new { Tenant = tenant, OtherTenant = otherTenant, MessageId = messageId });

            var invalidUser = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("""
                INSERT INTO PortalUsers
                    (Id, TenantId, Username, Email, PasswordHash, PasswordSalt, PasswordIterations, Context, Role, IsActive, CreatedAt)
                VALUES
                    (gen_random_uuid(), @MissingTenant, 'invalid-user', 'invalid@example.com',
                     decode('00','hex'), decode('00','hex'), 600000, 'tenant', 'user', TRUE, CURRENT_TIMESTAMP);
                """, new { MissingTenant = missingTenant }));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, invalidUser.SqlState);

            var crossTenantHistory = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("""
                INSERT INTO SmsMessageStatusHistory(Id, TenantId, MessageId, Status, CreatedAt)
                VALUES (gen_random_uuid(), @OtherTenant, @MessageId, 1, CURRENT_TIMESTAMP);
                """, new { OtherTenant = otherTenant, MessageId = messageId }));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, crossTenantHistory.SqlState);
        }
        finally
        {
            await connection.ExecuteAsync("""
                DELETE FROM SmsMessageStatusHistory WHERE MessageId=@MessageId;
                DELETE FROM SmsMessages WHERE Id=@MessageId;
                DELETE FROM PortalUsers WHERE TenantId IN (@Tenant, @OtherTenant);
                DELETE FROM Tenants WHERE Id IN (@Tenant, @OtherTenant);
                """, new { Tenant = tenant, OtherTenant = otherTenant, MessageId = messageId });
        }
    }

}

using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Sms.Infrastructure.Persistence;
using Sms.Infrastructure.Security;
using Sms.Seed;

namespace Sms.Infrastructure.Tests;

[Collection(PostgresTestCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ExampleProviderSeedSqlTests
{
    [PostgresFact]
    public async Task Seed_CanBeRepeated_EncryptsWithConfiguredKey_AndKeepsTenantIsolation()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = Environment.GetEnvironmentVariable("SMS_TEST_POSTGRES"),
            ["Encryption:MasterKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }).Build();
        var factory = new SqlConnectionFactory(configuration);
        var protector = new AesGcmSecretProtector(configuration);
        var configurationCache = TenantConfigurationCacheTestFactory.Create(factory);
        var providers = new TenantSmsProviderRepository(factory, protector, configurationCache);
        var tenant = Guid.NewGuid();
        using var connection = factory.CreateConnection();
        await connection.ExecuteAsync("INSERT INTO Tenants(Id,Name,IsActive,CreatedAt) VALUES(@Id,'Seed test',TRUE,CURRENT_TIMESTAMP);", new { Id = tenant });
        try
        {
            await ExampleProviders.SeedAsync(providers, tenant);
            await ExampleProviders.SeedAsync(providers, tenant);
            var rows = (await connection.QueryAsync<(string Provider, string ApiSecret, string? Settings)>(
                "SELECT Provider,ApiSecret,Settings FROM TenantSmsProviders WHERE TenantId=@Tenant;", new { Tenant = tenant })).ToList();
            Assert.Equal(3, rows.Count);
            Assert.Equal("Twilio", (await providers.GetDefaultAsync(tenant))!.Provider);
            Assert.Null(await providers.GetAsync(Guid.NewGuid(), "Twilio"));
            foreach (var row in rows)
            {
                var value = await providers.GetAsync(tenant, row.Provider);
                Assert.NotNull(value);
                Assert.True(value.IsActive);
                Assert.NotEqual(value.ApiSecret, row.ApiSecret);
                Assert.Equal(value.ApiSecret, protector.Unprotect(row.ApiSecret));
                Assert.StartsWith("fake-", value.ApiSecret);
            }
            var bandwidth = await providers.GetAsync(tenant, "Bandwidth");
            var encryptedSettings = rows.Single(row => row.Provider == "Bandwidth").Settings!;
            Assert.DoesNotContain("webhookPassword", encryptedSettings);
            Assert.Equal(bandwidth!.Settings, protector.Unprotect(encryptedSettings));
            using var settings = JsonDocument.Parse(bandwidth.Settings!);
            Assert.Equal("0000000", settings.RootElement.GetProperty("accountId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(settings.RootElement.GetProperty("applicationId").GetString()));
            Assert.Equal("fake-bandwidth-webhook-password", settings.RootElement.GetProperty("webhookPassword").GetString());
        }
        finally
        {
            await connection.ExecuteAsync("DELETE FROM TenantSmsProviders WHERE TenantId=@Tenant; DELETE FROM Tenants WHERE Id=@Tenant;", new { Tenant = tenant });
        }
    }
}

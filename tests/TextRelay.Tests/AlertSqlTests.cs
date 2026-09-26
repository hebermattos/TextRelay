using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Sms.Application.Alerts;
using Sms.Domain.Messages;
using Sms.Infrastructure.Persistence;

namespace Sms.Infrastructure.Tests;

[Collection(PostgresTestCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AlertSqlTests
{
    [PostgresFact]
    public async Task MatchingEvents_AlwaysFireAndPreserveTenantIsolation()
    {
        var connectionString = Environment.GetEnvironmentVariable("SMS_TEST_POSTGRES");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
            ["ConnectionStrings:ReportingPostgres"] = connectionString
        }).Build();
        var repository = new AlertRepository(new SqlConnectionFactory(configuration), new ReportingSqlConnectionFactory(configuration));
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        try
        {
            await connection.ExecuteAsync("""
                CREATE TABLE IF NOT EXISTS AlertMessageWindow
                (
                    EventId UUID PRIMARY KEY,
                    TenantId UUID NOT NULL,
                    Provider VARCHAR(50) NOT NULL,
                    Status INTEGER NOT NULL,
                    OccurredAtUtc TIMESTAMPTZ NOT NULL,
                    ExpiresAtUtc TIMESTAMPTZ NOT NULL
                );
                INSERT INTO Tenants(Id,Name,IsActive,CreatedAt) VALUES
                    (@Tenant,'Alert tenant',TRUE,CURRENT_TIMESTAMP),(@Other,'Other tenant',TRUE,CURRENT_TIMESTAMP);
                """, new { Tenant = tenantId, Other = otherTenantId });

            await repository.CreateRuleAsync(new(ruleId, tenantId, "Failures", "Twilio", SmsStatus.Failed, 15, true, now, null));

            await connection.ExecuteAsync("""
                INSERT INTO AlertMessageWindow(EventId,TenantId,Provider,Status,OccurredAtUtc,ExpiresAtUtc)
                VALUES(gen_random_uuid(),@Tenant,'Twilio',4,@Now,@Now + INTERVAL '24 hours');
                """, new { Tenant = tenantId, Now = now });

            await repository.EvaluateRuleAsync(Guid.NewGuid(), ruleId, tenantId, now);
            await repository.EvaluateRuleAsync(Guid.NewGuid(), ruleId, tenantId, now);

            Assert.Equal(2, (await repository.ListAlertsAsync(tenantId, false, 0, 20)).Count);
            Assert.Empty(await repository.ListAlertsAsync(otherTenantId, false, 0, 20));
        }
        finally
        {
            await connection.ExecuteAsync("""
                DELETE FROM AlertMessageWindow WHERE TenantId IN (@Tenant,@Other);
                DELETE FROM Alerts WHERE TenantId IN (@Tenant,@Other);
                DELETE FROM AlertRules WHERE TenantId IN (@Tenant,@Other);
                DELETE FROM Tenants WHERE Id IN (@Tenant,@Other);
                """, new { Tenant = tenantId, Other = otherTenantId });
        }
    }
}

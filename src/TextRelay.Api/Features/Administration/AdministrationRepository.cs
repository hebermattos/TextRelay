using Dapper;
using Npgsql;
using Sms.Application.Administration;
using Sms.Application.Auth;
using Sms.Application.Providers;
using Sms.Application.Security;

namespace Sms.Infrastructure.Persistence;

public sealed class AdministrationRepository(
    SqlConnectionFactory connectionFactory,
    ISecretProtector secretProtector,
    TenantConfigurationCache configurationCache) : IAdministrationRepository
{
    public async Task<IReadOnlyList<TenantSummary>> ListTenantsAsync(int skip, int take, CancellationToken cancellationToken)
    {
        // This cross-tenant metadata query is exposed only by the PlatformAdmin policy.
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/AdministrationRepository.ListTenantsAsync.01.sql");
        using var connection = connectionFactory.CreateConnection();

        var tenants = await connection.QueryAsync<TenantSummary>(new CommandDefinition(
            sql,
            new { Skip = skip, Take = take },
            cancellationToken: cancellationToken));

        return tenants.AsList();
    }

    public async Task<TenantSummary?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        (await configurationCache.GetAsync(tenantId, cancellationToken))?.Tenant;

    public async Task<bool> UpdateTenantAsync(Guid tenantId, string name, string timeZoneId, bool isActive, CancellationToken cancellationToken)
    {
        var previous = await configurationCache.GetAsync(tenantId, cancellationToken);
        using var connection = connectionFactory.CreateConnection();
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/AdministrationRepository.UpdateTenantAsync.08.sql"),
            new { TenantId = tenantId, Name = name, TimeZoneId = timeZoneId, IsActive = isActive },
            cancellationToken: cancellationToken)) == 1;

        if (updated)
            await configurationCache.InvalidateAsync(tenantId, previous, cancellationToken: CancellationToken.None);

        return updated;
    }

    public async Task<IReadOnlyList<ClientSummary>> ListClientsAsync(Guid tenantId, int skip, int take, CancellationToken cancellationToken)
    {
        var snapshot = await configurationCache.GetAsync(tenantId, cancellationToken);
        return snapshot?.Clients.Skip(skip).Take(take).ToArray() ?? Array.Empty<ClientSummary>();
    }

    public async Task CreateClientAsync(CreateApiClient client, CancellationToken cancellationToken)
    {
        var previous = await configurationCache.GetAsync(client.TenantId, cancellationToken);
        using var connection = connectionFactory.CreateConnection();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/AdministrationRepository.CreateClientAsync.03.sql"),
                new
                {
                    Id = Guid.NewGuid(),
                    client.TenantId,
                    client.ClientId,
                    client.SecretHash,
                    client.SecretSalt,
                    client.SecretIterations,
                    Now = DateTimeOffset.UtcNow
                },
                cancellationToken: cancellationToken));

            await configurationCache.InvalidateAsync(client.TenantId, previous, cancellationToken: CancellationToken.None);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new AdministrationConflictException();
        }
    }

    public async Task<bool> SetClientActiveAsync(Guid tenantId, Guid clientId, bool isActive, CancellationToken cancellationToken)
    {
        var previous = await configurationCache.GetAsync(tenantId, cancellationToken);
        using var connection = connectionFactory.CreateConnection();
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/AdministrationRepository.SetClientActiveAsync.04.sql"),
            new { TenantId = tenantId, ClientId = clientId, IsActive = isActive, Now = DateTimeOffset.UtcNow },
            cancellationToken: cancellationToken)) == 1;

        if (updated)
            await configurationCache.InvalidateAsync(tenantId, previous, cancellationToken: CancellationToken.None);

        return updated;
    }

    public async Task<string?> RotateClientSecretAsync(Guid tenantId, Guid clientId, byte[] hash, byte[] salt, int iterations, CancellationToken cancellationToken)
    {
        var previous = await configurationCache.GetAsync(tenantId, cancellationToken);
        using var connection = connectionFactory.CreateConnection();
        var clientIdentifier = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/AdministrationRepository.RotateClientSecretAsync.05.sql"),
            new { TenantId = tenantId, ClientId = clientId, Hash = hash, Salt = salt, Iterations = iterations, Now = DateTimeOffset.UtcNow },
            cancellationToken: cancellationToken));

        if (clientIdentifier is not null)
            await configurationCache.InvalidateAsync(tenantId, previous, cancellationToken: CancellationToken.None);

        return clientIdentifier;
    }

    public async Task<IReadOnlyList<TenantSmsProviderConfiguration>> ListProvidersAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var snapshot = await configurationCache.GetAsync(tenantId, cancellationToken);
        if (snapshot is null)
            return Array.Empty<TenantSmsProviderConfiguration>();

        return snapshot.Providers.Select(configuration => configuration with
        {
            ApiSecret = secretProtector.Unprotect(configuration.ApiSecret),
            Settings = string.IsNullOrWhiteSpace(configuration.Settings)
                ? null
                : secretProtector.Unprotect(configuration.Settings)
        }).ToArray();
    }
}

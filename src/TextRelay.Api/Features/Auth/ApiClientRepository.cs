using Dapper;
using Sms.Api.Features.Auth;

namespace Sms.Api.Features.Auth;

public sealed class ApiClientRepository(
    SqlConnectionFactory connectionFactory,
    TenantConfigurationCache configurationCache) : IApiClientRepository
{
    public Task<ApiClientCredential?> GetActiveByClientIdAsync(string clientId, CancellationToken cancellationToken = default) =>
        configurationCache.GetApiClientAsync(clientId, cancellationToken);

    public async Task CreateAsync(CreateApiClient client, CancellationToken cancellationToken = default)
    {
        var previous = await configurationCache.GetAsync(client.TenantId, cancellationToken);
        var sql = Sms.Api.Shared.Persistence.SqlQuery.Load("Persistence/ApiClientRepository.CreateAsync.02.sql");
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = Guid.NewGuid(),
            client.TenantId,
            client.ClientId,
            client.SecretHash,
            client.SecretSalt,
            client.SecretIterations,
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken: cancellationToken));

        await configurationCache.InvalidateAsync(client.TenantId, previous, cancellationToken: CancellationToken.None);
    }
}

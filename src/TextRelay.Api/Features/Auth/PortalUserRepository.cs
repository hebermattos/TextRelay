using Dapper;
using Sms.Api.Features.Auth;

namespace Sms.Api.Features.Auth;

public sealed class PortalUserRepository(SqlConnectionFactory connections) : IPortalUserRepository
{
    public async Task<PortalUserAccount?> GetActiveByUsernameAsync(
        string username,
        string context,
        string? tenantCode,
        CancellationToken cancellationToken = default)
    {
        var sql = Sms.Api.Shared.Persistence.SqlQuery.Load("Persistence/PortalUserRepository.GetActiveByUsernameAsync.01.sql");

        using var connection = connections.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<PortalUserAccount>(
            new CommandDefinition(sql, new { Username = username, Context = context, TenantCode = tenantCode },
                cancellationToken: cancellationToken));
    }

    public async Task<PortalUserAccount?> GetActiveByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var sql = Sms.Api.Shared.Persistence.SqlQuery.Load("Persistence/PortalUserRepository.GetActiveByIdAsync.02.sql");

        using var connection = connections.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<PortalUserAccount>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }
}

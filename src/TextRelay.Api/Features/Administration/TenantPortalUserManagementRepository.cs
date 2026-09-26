using Dapper;
using Npgsql;
using Sms.Application.Auth;

namespace Sms.Infrastructure.Persistence;

public sealed class TenantPortalUserManagementRepository(SqlConnectionFactory connections)
    : ITenantPortalUserManagementRepository
{
    public async Task<IReadOnlyList<PortalUserSummary>> ListAsync(
        Guid tenantId, int skip = 0, int take = 20, string? search = null, string? role = null, bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = connections.CreateConnection();
        return (await connection.QueryAsync<PortalUserSummary>(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/TenantPortalUserManagementRepository.ListAsync.01.sql"),
            new { TenantId = tenantId, Skip = skip, Take = take, Search = search, Role = role, IsActive = isActive },
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<Guid> CreateAsync(
        NewPortalUser user, CancellationToken cancellationToken = default)
    {
        using var connection = connections.CreateConnection();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/TenantPortalUserManagementRepository.CreateAsync.02.sql"), user, cancellationToken: cancellationToken));
            return user.Id;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new PortalUserConflictException();
        }
    }

    public async Task<bool> UpdateAsync(
        Guid tenantId, Guid id, string username, string email, string role,
        CancellationToken cancellationToken = default)
    {
        using var connection = connections.CreateConnection();
        try
        {
            return await connection.ExecuteAsync(new CommandDefinition(
                Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/TenantPortalUserManagementRepository.UpdateAsync.05.sql"),
                new { TenantId = tenantId, Id = id, Username = username, Email = email, Role = role },
                cancellationToken: cancellationToken)) == 1;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new PortalUserConflictException();
        }
    }

    public async Task<bool> SetActiveAsync(
        Guid tenantId, Guid id, bool isActive,
        CancellationToken cancellationToken = default)
    {
        using var connection = connections.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/TenantPortalUserManagementRepository.SetActiveAsync.03.sql"), new { TenantId = tenantId, Id = id, IsActive = isActive },
            cancellationToken: cancellationToken)) == 1;
    }

    public async Task<bool> ResetPasswordAsync(
        Guid tenantId, Guid id, byte[] hash, byte[] salt, int iterations,
        CancellationToken cancellationToken = default)
    {
        using var connection = connections.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/TenantPortalUserManagementRepository.ResetPasswordAsync.04.sql"), new { TenantId = tenantId, Id = id, Hash = hash, Salt = salt, Iterations = iterations },
            cancellationToken: cancellationToken)) == 1;
    }
}

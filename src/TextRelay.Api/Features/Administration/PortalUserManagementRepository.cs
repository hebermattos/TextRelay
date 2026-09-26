using Dapper;
using Npgsql;
using Sms.Application.Auth;

namespace Sms.Infrastructure.Persistence;

public sealed class PortalUserManagementRepository(SqlConnectionFactory connections)
    : IPortalUserManagementRepository
{
    public async Task<IReadOnlyList<PortalUserSummary>> ListPlatformUsersAsync(
        int skip = 0, int take = 20, string? search = null, string? role = null, bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = connections.CreateConnection();
        return (await connection.QueryAsync<PortalUserSummary>(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/PortalUserManagementRepository.ListPlatformUsersAsync.01.sql"),
            new { Skip = skip, Take = take, Search = search, Role = role, IsActive = isActive },
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<Guid> CreatePlatformUserAsync(
        NewPortalUser user,
        CancellationToken cancellationToken = default)
    {
        using var connection = connections.CreateConnection();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/PortalUserManagementRepository.CreatePlatformUserAsync.02.sql"), user, cancellationToken: cancellationToken));
            return user.Id;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new PortalUserConflictException();
        }
    }

    public async Task<bool> SetActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        using var connection = connections.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/PortalUserManagementRepository.SetActiveAsync.03.sql"), new { Id = id, IsActive = isActive },
            cancellationToken: cancellationToken)) == 1;
    }

    public async Task<bool> ResetPasswordAsync(
        Guid id,
        byte[] hash,
        byte[] salt,
        int iterations,
        CancellationToken cancellationToken = default)
    {
        using var connection = connections.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/PortalUserManagementRepository.ResetPasswordAsync.04.sql"), new { Id = id, Hash = hash, Salt = salt, Iterations = iterations },
            cancellationToken: cancellationToken)) == 1;
    }
}

using Dapper;
using Npgsql;
using Sms.Application.Auth;
using Sms.Application.Tenants;
using Sms.Application.Administration;

namespace Sms.Infrastructure.Persistence;

public sealed class TenantProvisioner(SqlConnectionFactory connectionFactory) : ITenantProvisioner
{
    public async Task CreateAsync(Guid tenantId, string name, CreateApiClient client, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateSqlConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            await connection.ExecuteAsync(new CommandDefinition(
                Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/TenantProvisioner.CreateAsync.02.sql"),
                new { Id = tenantId, Name = name, Code = client.ClientId, Now = now }, transaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/TenantProvisioner.CreateAsync.01.sql"),
                new
                {
                    Id = Guid.NewGuid(), client.TenantId, client.ClientId, client.SecretHash,
                    client.SecretSalt, client.SecretIterations, Now = now
                }, transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new AdministrationConflictException();
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}

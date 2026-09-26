using Dapper;
using Sms.Application.Tenants;

namespace Sms.Infrastructure.Persistence;

public sealed class TenantRepository(SqlConnectionFactory connectionFactory) : ITenantRepository
{
    public async Task CreateAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/TenantRepository.CreateAsync.01.sql");
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, Name = name, CreatedAt = DateTimeOffset.UtcNow }, cancellationToken: cancellationToken));
    }
}

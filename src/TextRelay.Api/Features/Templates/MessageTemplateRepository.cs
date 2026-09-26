using Dapper;
using Sms.Application.Templates;

namespace Sms.Infrastructure.Persistence;

public sealed class MessageTemplateRepository(SqlConnectionFactory connectionFactory) : IMessageTemplateRepository
{
    public async Task<IReadOnlyList<MessageTemplate>> ListAsync(Guid tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var sql = Sql.SqlQuery.Load("Persistence/MessageTemplateRepository.ListAsync.01.sql");
        return (await connection.QueryAsync<MessageTemplate>(new CommandDefinition(sql, new { TenantId = tenantId, Skip = skip, Take = take }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<MessageTemplate?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var sql = Sql.SqlQuery.Load("Persistence/MessageTemplateRepository.GetAsync.02.sql");
        return await connection.QuerySingleOrDefaultAsync<MessageTemplate>(new CommandDefinition(sql, new { TenantId = tenantId, Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<MessageTemplate> CreateAsync(Guid tenantId, string name, string body, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var sql = Sql.SqlQuery.Load("Persistence/MessageTemplateRepository.CreateAsync.03.sql");
        return await connection.QuerySingleAsync<MessageTemplate>(new CommandDefinition(sql, new { Id = Guid.NewGuid(), TenantId = tenantId, Name = name, Body = body }, cancellationToken: cancellationToken));
    }

    public async Task<MessageTemplate?> UpdateAsync(Guid tenantId, Guid id, string name, string body, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var sql = Sql.SqlQuery.Load("Persistence/MessageTemplateRepository.UpdateAsync.04.sql");
        return await connection.QuerySingleOrDefaultAsync<MessageTemplate>(new CommandDefinition(sql, new { TenantId = tenantId, Id = id, Name = name, Body = body }, cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var sql = Sql.SqlQuery.Load("Persistence/MessageTemplateRepository.DeleteAsync.05.sql");
        return await connection.ExecuteAsync(new CommandDefinition(sql, new { TenantId = tenantId, Id = id }, cancellationToken: cancellationToken)) > 0;
    }
}

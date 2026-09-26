using Dapper;
using Sms.Infrastructure.Messaging;

namespace Sms.Infrastructure.Persistence;

public sealed class TenantSmsOverviewOutbox(SqlConnectionFactory connectionFactory) : ITenantSmsOverviewOutbox
{
    public async Task<IReadOnlyList<TenantSmsOverviewEvent>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var events = await connection.QueryAsync<TenantSmsOverviewEvent>(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Messaging/TenantSmsOverviewOutboxPublisher.PublishBatchAsync.01.sql"),
            cancellationToken: cancellationToken));
        return events.AsList();
    }

    public async Task MarkPublishedAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Messaging/TenantSmsOverviewOutboxPublisher.PublishBatchAsync.02.sql"),
            new { EventId = eventId }, cancellationToken: cancellationToken));
    }
}

using System.Data;
using Dapper;
using Sms.Infrastructure.Messaging;

namespace Sms.Infrastructure.Persistence;

public sealed class TenantSmsOverviewProjection(ReportingSqlConnectionFactory connectionFactory) : ITenantSmsOverviewProjection
{
    public async Task ApplyAsync(TenantSmsOverviewEvent item, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        var inserted = await connection.ExecuteAsync(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Messaging/TenantSmsOverviewConsumer.Consume.01.sql"),
            new { item.EventId }, transaction, cancellationToken: cancellationToken));
        if (inserted == 0) { transaction.Commit(); return; }

        await connection.ExecuteAsync(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Messaging/TenantSmsOverviewConsumer.Consume.02.sql"),
            item, transaction, cancellationToken: cancellationToken));
        transaction.Commit();
    }
}

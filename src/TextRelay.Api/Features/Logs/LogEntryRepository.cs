using Dapper;
using Sms.Application.Logs;

namespace Sms.Infrastructure.Persistence;

public sealed class LogEntryRepository(LogsSqlConnectionFactory connectionFactory) : ILogEntryRepository
{
    public async Task<IReadOnlyList<LogEntry>> GetActivityAsync(
        Guid tenantId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        LogCursor? cursor,
        int take,
        CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/LogEntryRepository.GetActivityAsync.01.sql");

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<LogEntry>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, From = from, To = to, CursorTimestamp = cursor?.Timestamp, CursorId = cursor?.Id, Take = take },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<LogEntry>> GetSystemAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        LogCursor? cursor,
        int take,
        CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/LogEntryRepository.GetSystemAsync.02.sql");

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<LogEntry>(new CommandDefinition(
            sql,
            new { From = from, To = to, CursorTimestamp = cursor?.Timestamp, CursorId = cursor?.Id, Take = take },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }
}

using Dapper;
using Sms.Domain.Messages;
using Sms.Infrastructure.Persistence;

namespace Sms.Infrastructure.Messaging;

public interface ISmsQueuePublishSource
{
    Task<IReadOnlyList<SmsQueuePublishMessage>> GetPendingAsync(CancellationToken cancellationToken = default);
    Task<bool> TryClaimAsync(SmsQueuePublishMessage message, CancellationToken cancellationToken = default);
    Task ReleaseAsync(SmsQueuePublishMessage message, CancellationToken cancellationToken = default);
}

public sealed record SmsQueuePublishMessage(
    Guid MessageId,
    Guid TenantId,
    SmsQueueStatus OriginalQueueStatus,
    DateTimeOffset? UpdatedAt);

public sealed class SmsQueuePublishSource(SqlConnectionFactory connectionFactory) : ISmsQueuePublishSource
{
    public async Task<IReadOnlyList<SmsQueuePublishMessage>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SmsQueuePublishMessage>(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Messaging/SmsQueuePublishSource.GetPendingAsync.01.sql"),
            new
            {
                NotQueued = SmsQueueStatus.NotQueued,
                Queued = SmsQueueStatus.Queued,
                Scheduled = SmsQueueStatus.Scheduled
            },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<bool> TryClaimAsync(SmsQueuePublishMessage message, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Messaging/SmsQueuePublishSource.TryClaimAsync.01.sql"),
            new
            {
                message.TenantId,
                message.MessageId,
                ExpectedQueueStatus = message.OriginalQueueStatus,
                ExpectedUpdatedAt = message.UpdatedAt,
                Queued = SmsQueueStatus.Queued,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            cancellationToken: cancellationToken));
        return affected == 1;
    }

    public async Task ReleaseAsync(SmsQueuePublishMessage message, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Messaging/SmsQueuePublishSource.ReleaseAsync.01.sql"),
            new
            {
                message.TenantId,
                message.MessageId,
                Queued = SmsQueueStatus.Queued,
                ReleasedQueueStatus = message.OriginalQueueStatus == SmsQueueStatus.Queued
                    ? SmsQueueStatus.NotQueued
                    : message.OriginalQueueStatus,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            cancellationToken: cancellationToken));
    }
}

using Dapper;
using Sms.Application.Messages;
using Sms.Application.Security;
using Sms.Domain.Messages;

namespace Sms.Infrastructure.Persistence;

public sealed class SmsMessageRepository(SqlConnectionFactory connectionFactory, ISmsContentProtector protector) : ISmsMessageRepository
{
    public async Task<SmsMessage?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/SmsMessageRepository.GetByIdAsync.01.sql");
        using var connection = connectionFactory.CreateConnection();
        var message = await connection.QuerySingleOrDefaultAsync<SmsMessage>(new CommandDefinition(sql, new { TenantId = tenantId, Id = id }, cancellationToken: cancellationToken));
        return message is null ? null : Decrypt(message);
    }

    public async Task<IReadOnlyList<SmsMessage>> GetHistoryAsync(Guid tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/SmsMessageRepository.GetHistoryAsync.02.sql");
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SmsMessage>(new CommandDefinition(sql, new { TenantId = tenantId, Skip = skip, Take = take }, cancellationToken: cancellationToken));
        return rows.Select(Decrypt).ToArray();
    }

    public async Task<IReadOnlyList<SmsStatusHistory>> GetStatusHistoryAsync(Guid tenantId, Guid messageId, CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/SmsMessageRepository.GetStatusHistoryAsync.03.sql");
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SmsStatusHistory>(new CommandDefinition(sql, new { TenantId = tenantId, MessageId = messageId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task InsertAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/SmsMessageRepository.InsertAsync.04.sql");
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, EncryptedParameters(message), cancellationToken: cancellationToken));
    }

    public async Task InsertInboundIfNotExistsAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/SmsMessageRepository.InsertInboundIfNotExistsAsync.05.sql");
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, EncryptedParameters(message), cancellationToken: cancellationToken));
    }

    public async Task<bool> TryQueueScheduledAsync(Guid tenantId, Guid id, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/SmsMessageRepository.TryQueueScheduledAsync.08.sql"),
            new { TenantId = tenantId, Id = id, Scheduled = SmsQueueStatus.Scheduled, Queued = SmsQueueStatus.Queued, UpdatedAt = updatedAt },
            cancellationToken: cancellationToken)) != 0;
    }

    public async Task<bool> TryClaimQueuedAsync(Guid tenantId, Guid id, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/SmsMessageRepository.TryClaimQueuedAsync.10.sql"),
            new { TenantId = tenantId, Id = id, Queued = SmsQueueStatus.Queued, Processing = SmsQueueStatus.Processing, Pending = SmsStatus.Pending, UpdatedAt = updatedAt },
            cancellationToken: cancellationToken)) != 0;
    }

    public async Task UpdateQueueStatusAsync(Guid tenantId, Guid id, SmsQueueStatus queueStatus, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/SmsMessageRepository.UpdateQueueStatusAsync.09.sql");
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { TenantId = tenantId, Id = id, QueueStatus = queueStatus, UpdatedAt = updatedAt }, cancellationToken: cancellationToken));
    }

    public async Task UpdateStatusAsync(Guid tenantId, Guid id, SmsStatus status, string? providerMessageId, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/SmsMessageRepository.UpdateStatusAsync.06.sql");
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { TenantId = tenantId, Id = id, Status = status, ProviderMessageId = providerMessageId, UpdatedAt = updatedAt }, cancellationToken: cancellationToken));
    }

    public async Task UpdateStatusByProviderMessageIdAsync(Guid tenantId, string provider, string providerMessageId, SmsStatus status, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/SmsMessageRepository.UpdateStatusByProviderMessageIdAsync.07.sql");
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            TenantId = tenantId, Provider = provider, ProviderMessageId = providerMessageId, Status = status, UpdatedAt = updatedAt,
            Pending = SmsStatus.Pending, Sent = SmsStatus.Sent, Delivered = SmsStatus.Delivered, Failed = SmsStatus.Failed
        }, cancellationToken: cancellationToken));
    }

    private object EncryptedParameters(SmsMessage message) => new
    {
        message.Id,
        message.TenantId,
        message.UserId,
        From = protector.Protect(message.TenantId, message.Id, nameof(message.From), message.From),
        To = protector.Protect(message.TenantId, message.Id, nameof(message.To), message.To),
        Body = protector.Protect(message.TenantId, message.Id, nameof(message.Body), message.Body),
        message.Provider,
        message.ProviderMessageId,
        message.Direction,
        message.QueueStatus,
        message.Status,
        message.CreatedAt,
        message.ScheduledAtUtc,
        message.UpdatedAt
    };

    private SmsMessage Decrypt(SmsMessage message) => new()
    {
        Id = message.Id,
        TenantId = message.TenantId,
        UserId = message.UserId,
        From = protector.Unprotect(message.TenantId, message.Id, nameof(message.From), message.From),
        To = protector.Unprotect(message.TenantId, message.Id, nameof(message.To), message.To),
        Body = protector.Unprotect(message.TenantId, message.Id, nameof(message.Body), message.Body),
        Provider = message.Provider,
        ProviderMessageId = message.ProviderMessageId,
        Direction = message.Direction,
        QueueStatus = message.QueueStatus,
        Status = message.Status,
        CreatedAt = message.CreatedAt,
        ScheduledAtUtc = message.ScheduledAtUtc,
        UpdatedAt = message.UpdatedAt
    };
}

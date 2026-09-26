using Dapper;
using Sms.Application.OptOut;
using Sms.Application.Security;

namespace Sms.Infrastructure.Persistence;

public sealed class OptOutRepository(
    SqlConnectionFactory connectionFactory,
    ISmsContentProtector protector) : IOptOutRepository
{
    public async Task<IReadOnlyList<BlockedNumber>> ListAsync(Guid tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/OptOutRepository.ListAsync.01.sql");
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(sql, new { TenantId = tenantId, Skip = skip, Take = take }, cancellationToken: cancellationToken));
        return rows.Select(row => new BlockedNumber(row.Id,
            protector.Unprotect(tenantId, row.Id, "PhoneNumber", row.PhoneNumber),
            row.Source, row.Reason, row.CreatedAt, row.UpdatedAt)).ToArray();
    }

    public async Task<bool> IsBlockedAsync(Guid tenantId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/OptOutRepository.IsBlockedAsync.02.sql");
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(sql,
            new { TenantId = tenantId, PhoneHash = Hash(tenantId, phoneNumber) }, cancellationToken: cancellationToken));
    }

    public async Task AddOrUpdateAsync(Guid tenantId, string phoneNumber, string source, string? reason, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/OptOutRepository.AddOrUpdateAsync.03.sql");
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id, TenantId = tenantId, PhoneHash = Hash(tenantId, phoneNumber),
            PhoneNumber = protector.Protect(tenantId, id, "PhoneNumber", phoneNumber),
            Source = source, Reason = reason, OccurredAt = occurredAt
        }, cancellationToken: cancellationToken));
    }

    public async Task<bool> RemoveAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/OptOutRepository.RemoveAsync.04.sql");
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(sql, new { TenantId = tenantId, Id = id }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task RemoveByPhoneAsync(Guid tenantId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/OptOutRepository.RemoveByPhoneAsync.05.sql");
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql,
            new { TenantId = tenantId, PhoneHash = Hash(tenantId, phoneNumber) }, cancellationToken: cancellationToken));
    }

    private byte[] Hash(Guid tenantId, string phoneNumber) => protector.Fingerprint(tenantId, "opt-out-phone-v1", phoneNumber);
    private sealed record Row(Guid Id, string PhoneNumber, string Source, string? Reason, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
}

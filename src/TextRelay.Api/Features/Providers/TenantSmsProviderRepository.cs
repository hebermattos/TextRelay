using Dapper;
using Npgsql;
using Sms.Application.Administration;
using Sms.Application.Providers;
using Sms.Application.Security;

namespace Sms.Infrastructure.Persistence;

public sealed class TenantSmsProviderRepository(
    SqlConnectionFactory connectionFactory,
    ISecretProtector secretProtector,
    TenantConfigurationCache configurationCache) : ITenantSmsProviderRepository
{
    public async Task<TenantSmsProviderConfiguration?> GetAsync(Guid tenantId, string provider, CancellationToken cancellationToken = default)
    {
        var snapshot = await configurationCache.GetAsync(tenantId, cancellationToken);
        var value = snapshot?.Providers.FirstOrDefault(x =>
            x.IsActive && string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase));
        return Decrypt(value);
    }

    public async Task<TenantSmsProviderConfiguration?> GetDefaultAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var snapshot = await configurationCache.GetAsync(tenantId, cancellationToken);
        return Decrypt(snapshot?.Providers.SingleOrDefault(x => x.IsActive && x.IsDefault));
    }

    public async Task<TenantSmsProviderConfiguration?> GetByAccountAndNumberAsync(
        string provider,
        string accountId,
        string number,
        CancellationToken cancellationToken = default) =>
        Decrypt(await configurationCache.GetProviderByRouteAsync(provider, accountId, number, cancellationToken));

    public async Task UpsertAsync(TenantSmsProviderConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var previous = await configurationCache.GetAsync(configuration.TenantId, cancellationToken);
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/TenantSmsProviderRepository.UpsertAsync.01.sql");
        using var connection = connectionFactory.CreateSqlConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    Id = Guid.NewGuid(),
                    configuration.TenantId,
                    configuration.Provider,
                    configuration.AccountId,
                    ApiSecret = secretProtector.Protect(configuration.ApiSecret),
                    FromNumber = configuration.FromNumber?.Trim(),
                    configuration.IsDefault,
                    configuration.IsActive,
                    Settings = ProtectOptional(configuration.Settings),
                    Now = DateTimeOffset.UtcNow
                },
                transaction: transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);

            await configurationCache.InvalidateAsync(
                configuration.TenantId,
                previous,
                configuration,
                CancellationToken.None);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new AdministrationConflictException();
        }
    }

    private string? ProtectOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : secretProtector.Protect(value);

    private string? UnprotectOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : secretProtector.Unprotect(value);

    private TenantSmsProviderConfiguration? Decrypt(TenantSmsProviderConfiguration? value) =>
        value is null
            ? null
            : value with
            {
                ApiSecret = secretProtector.Unprotect(value.ApiSecret),
                Settings = UnprotectOptional(value.Settings)
            };
}

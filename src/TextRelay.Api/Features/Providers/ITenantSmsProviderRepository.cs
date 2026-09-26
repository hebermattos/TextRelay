namespace Sms.Application.Providers;

public sealed record TenantSmsProviderConfiguration(
    Guid TenantId,
    string Provider,
    string AccountId,
    string ApiSecret,
    string? FromNumber,
    bool IsDefault,
    bool IsActive,
    string? Settings = null);

public interface ITenantSmsProviderRepository
{
    Task<TenantSmsProviderConfiguration?> GetAsync(Guid tenantId, string provider, CancellationToken cancellationToken = default);
    Task<TenantSmsProviderConfiguration?> GetDefaultAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<TenantSmsProviderConfiguration?> GetByAccountAndNumberAsync(string provider, string accountId, string number, CancellationToken cancellationToken = default);
    Task UpsertAsync(TenantSmsProviderConfiguration configuration, CancellationToken cancellationToken = default);
}

using Sms.Application.Auth;
using Sms.Application.Providers;

namespace Sms.Application.Administration;

public sealed record TenantSummary(Guid Id, string Name, string TimeZoneId, bool IsActive, DateTimeOffset CreatedAt);
public sealed record ClientSummary(Guid Id, string ClientId, bool IsActive, DateTimeOffset CreatedAt);
public sealed record IssuedClientSecret(string ClientId, string ClientSecret);
public sealed record ProviderField(string Key, string Label, bool Secret, bool Required);
public sealed record ProviderDefinition(string Name, string AccountLabel, string SecretLabel, IReadOnlyList<ProviderField> Fields);
public sealed record ProviderEdit(string AccountId, string FromNumber, bool IsActive, bool IsDefault,
    string? ApiSecret, Dictionary<string, string?>? Settings);
public sealed record ProviderSummary(string Provider, string AccountId, string? FromNumber, bool IsActive,
    bool IsDefault, bool HasApiSecret, IReadOnlyDictionary<string, string?> Settings, IReadOnlyList<string> ConfiguredSecrets);
public sealed class AdministrationConflictException() : Exception("Já existe um cadastro com estes identificadores.");

public interface IProviderCatalogCache
{
    Task<IReadOnlyList<ProviderDefinition>> GetAsync(CancellationToken cancellationToken = default);
}

public interface IProviderSettingsPolicy
{
    ProviderDefinition Definition { get; }
    string? MergeAndValidate(string? existing, IReadOnlyDictionary<string, string?> changes);
    (IReadOnlyDictionary<string, string?> Values, IReadOnlyList<string> ConfiguredSecrets) Describe(string? settings);
}

public interface IAdministrationRepository
{
    Task<IReadOnlyList<TenantSummary>> ListTenantsAsync(int skip, int take, CancellationToken cancellationToken);
    Task<TenantSummary?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<bool> UpdateTenantAsync(Guid tenantId, string name, string timeZoneId, bool isActive, CancellationToken cancellationToken);
    Task<IReadOnlyList<ClientSummary>> ListClientsAsync(Guid tenantId, int skip, int take, CancellationToken cancellationToken);
    Task CreateClientAsync(CreateApiClient client, CancellationToken cancellationToken);
    Task<bool> SetClientActiveAsync(Guid tenantId, Guid clientId, bool isActive, CancellationToken cancellationToken);
    Task<string?> RotateClientSecretAsync(Guid tenantId, Guid clientId, byte[] hash, byte[] salt, int iterations, CancellationToken cancellationToken);
    Task<IReadOnlyList<TenantSmsProviderConfiguration>> ListProvidersAsync(Guid tenantId, CancellationToken cancellationToken);
}

public sealed record AvailableProvider(string Name, string? FromNumber, bool IsDefault);
public sealed record TenantOverview(string Name, string TimeZoneId, long Outbound, long Inbound, long Delivered, long Failed,
    long Pending, IReadOnlyList<AvailableProvider> Providers);

public interface ITenantPortalRepository
{
    Task<TenantOverview?> GetOverviewAsync(Guid tenantId, CancellationToken cancellationToken);
}

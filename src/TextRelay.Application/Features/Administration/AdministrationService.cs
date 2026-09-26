using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Sms.Application.Auth;
using Sms.Application.Providers;

namespace Sms.Application.Administration;

public sealed class AdministrationService(IAdministrationRepository repository,
    ITenantSmsProviderRepository providers, IEnumerable<IProviderSettingsPolicy> policies, IProviderCatalogCache providerCatalogCache,
    ITenantRateLimitRepository rateLimits)
{
    public Task<IReadOnlyList<ProviderDefinition>> GetProviderCatalogAsync(CancellationToken cancellationToken = default) =>
        providerCatalogCache.GetAsync(cancellationToken);

    public Task<IReadOnlyList<TenantSummary>> ListTenantsAsync(int skip, int take, CancellationToken cancellationToken)
    {
        ValidatePage(skip, take);
        return repository.ListTenantsAsync(skip, take, cancellationToken);
    }

    public async Task<TenantSummary> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await repository.GetTenantAsync(tenantId, cancellationToken) ?? throw new KeyNotFoundException();

    public async Task UpdateTenantAsync(Guid tenantId, string name, string timeZoneId, bool isActive, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw new ArgumentException("Enter a name with up to 200 characters.");
        if (!TimeZoneCatalog.IsValid(timeZoneId))
            throw new ArgumentException("Enter a valid IANA time zone identifier.");
        if (!await repository.UpdateTenantAsync(tenantId, name.Trim(), timeZoneId.Trim(), isActive, cancellationToken))
            throw new KeyNotFoundException();
    }

    public static IReadOnlyList<string> TimeZones => TimeZoneCatalog.Ids;

    public async Task<TenantRateLimitSettings> GetRateLimitsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await GetTenantAsync(tenantId, cancellationToken);
        return await rateLimits.GetAsync(tenantId, cancellationToken);
    }

    public async Task UpdateRateLimitsAsync(Guid tenantId, TenantRateLimitSettings settings, CancellationToken cancellationToken)
    {
        await GetTenantAsync(tenantId, cancellationToken);
        if (settings.RequestsPerMinute is < 1 or > 100000 || settings.SmsPerMinute is < 1 or > 100000 || settings.OllamaRequestsPerMinute is < 1 or > 100000)
            throw new ArgumentException("Rate limits must be between 1 and 100000 requests per minute.");
        await rateLimits.SaveAsync(tenantId, settings, cancellationToken);
    }

    public async Task<IReadOnlyList<ClientSummary>> ListClientsAsync(Guid tenantId, int skip, int take, CancellationToken cancellationToken)
    {
        ValidatePage(skip, take);
        await GetTenantAsync(tenantId, cancellationToken);
        return await repository.ListClientsAsync(tenantId, skip, take, cancellationToken);
    }

    public async Task<IssuedClientSecret> CreateClientAsync(Guid tenantId, string? requestedId, CancellationToken cancellationToken)
    {
        await GetTenantAsync(tenantId, cancellationToken);
        var clientId = string.IsNullOrWhiteSpace(requestedId) ? $"client_{Guid.NewGuid():N}" : requestedId.Trim();
        if (!Regex.IsMatch(clientId, "^[a-zA-Z0-9_-]{1,100}$"))
            throw new ArgumentException("The identifier must contain up to 100 letters, numbers, hyphens, or underscores.");
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var hashed = ClientSecretHasher.Hash(secret);
        await repository.CreateClientAsync(new(tenantId, clientId, hashed.Hash, hashed.Salt, hashed.Iterations), cancellationToken);
        return new(clientId, secret);
    }

    public async Task SetClientActiveAsync(Guid tenantId, Guid clientId, bool isActive, CancellationToken cancellationToken)
    {
        if (!await repository.SetClientActiveAsync(tenantId, clientId, isActive, cancellationToken))
            throw new KeyNotFoundException();
    }

    public async Task<IssuedClientSecret> RotateClientSecretAsync(Guid tenantId, Guid clientId, CancellationToken cancellationToken)
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var hashed = ClientSecretHasher.Hash(secret);
        var name = await repository.RotateClientSecretAsync(tenantId, clientId, hashed.Hash, hashed.Salt, hashed.Iterations, cancellationToken)
            ?? throw new KeyNotFoundException();
        return new(name, secret);
    }

    public async Task<IReadOnlyList<ProviderSummary>> ListProvidersAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await GetTenantAsync(tenantId, cancellationToken);
        var configurations = await repository.ListProvidersAsync(tenantId, cancellationToken);
        return configurations.Select(configuration =>
        {
            var policy = GetPolicy(configuration.Provider);
            var description = policy.Describe(configuration.Settings);
            return new ProviderSummary(configuration.Provider, configuration.AccountId, configuration.FromNumber,
                configuration.IsActive, configuration.IsDefault, !string.IsNullOrEmpty(configuration.ApiSecret),
                description.Values, description.ConfiguredSecrets);
        }).ToArray();
    }

    public async Task SaveProviderAsync(Guid tenantId, string provider, ProviderEdit input, CancellationToken cancellationToken)
    {
        await GetTenantAsync(tenantId, cancellationToken);
        var policy = GetPolicy(provider);
        if (string.IsNullOrWhiteSpace(input.AccountId) || input.AccountId.Trim().Length > 200)
            throw new ArgumentException("Enter the provider account with up to 200 characters.");
        if (string.IsNullOrWhiteSpace(input.FromNumber) || !Regex.IsMatch(input.FromNumber, "^\\+[1-9][0-9]{6,14}$"))
            throw new ArgumentException("Enter the sender in international format, for example +5511999999999.");
        if (input.IsDefault && !input.IsActive) throw new ArgumentException("The default provider must be active.");
        var existing = (await repository.ListProvidersAsync(tenantId, cancellationToken))
            .SingleOrDefault(x => x.Provider == policy.Definition.Name);
        var secret = string.IsNullOrWhiteSpace(input.ApiSecret) ? existing?.ApiSecret : input.ApiSecret;
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) > 512)
            throw new ArgumentException("Enter the provider credential, up to 512 UTF-8 bytes.");
        var settings = policy.MergeAndValidate(existing?.Settings, input.Settings ?? new());
        await providers.UpsertAsync(new(tenantId, policy.Definition.Name, input.AccountId.Trim(), secret,
            input.FromNumber, input.IsDefault, input.IsActive, settings), cancellationToken);
    }

    private IProviderSettingsPolicy GetPolicy(string name) => policies.FirstOrDefault(x => x.Definition.Name == name)
        ?? throw new ArgumentException("Provider is not supported.");

    private static void ValidatePage(int skip, int take)
    {
        if (skip < 0 || take is < 1 or > 100) throw new ArgumentException("Invalid pagination.");
    }
}

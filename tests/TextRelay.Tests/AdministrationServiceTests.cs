using System.Text.Json;
using Sms.Application.Administration;
using Sms.Application.Auth;
using Sms.Application.Providers;
using Sms.Infrastructure.Providers;

namespace Sms.Infrastructure.Tests;

public sealed class AdministrationServiceTests
{
    [Fact]
    public async Task ClientSecrets_AreUniqueAndOnlyHashesReachPersistence()
    {
        var repo = new AdministrationFakeRepository(); var service = Service(repo);
        var first = await service.CreateClientAsync(repo.Tenant.Id, "client-one", default);
        var stored = repo.CreatedClient!;
        Assert.True(ClientSecretHasher.Verify(first.ClientSecret, stored.SecretHash, stored.SecretSalt, stored.SecretIterations));
        Assert.Equal(repo.Tenant.Id, stored.TenantId);
        var second = await service.CreateClientAsync(repo.Tenant.Id, null, default);
        Assert.NotEqual(first.ClientSecret, second.ClientSecret);
        Assert.NotEqual(stored.SecretSalt, repo.CreatedClient!.SecretSalt);
        Assert.StartsWith("client_", second.ClientId);
        var rotated = await service.RotateClientSecretAsync(repo.Tenant.Id, repo.ClientId, default);
        Assert.True(ClientSecretHasher.Verify(rotated.ClientSecret, repo.RotatedHash!, repo.RotatedSalt!, repo.RotatedIterations));
        Assert.Equal(repo.Tenant.Id, repo.LastTenant);
        Assert.Equal(repo.ClientId, repo.LastClient);
    }

    [Fact]
    public async Task UnknownTenantOrClient_CannotBeModified()
    {
        var repo = new AdministrationFakeRepository(); var service = Service(repo); var missing = Guid.NewGuid();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetTenantAsync(missing, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateClientAsync(missing, null, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ListClientsAsync(missing, 0, 20, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ListProvidersAsync(missing, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateTenantAsync(missing, "Name", "UTC", true, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetClientActiveAsync(missing, repo.ClientId, false, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RotateClientSecretAsync(missing, repo.ClientId, default));
        Assert.Null(repo.CreatedClient);
    }

    [Theory]
    [InlineData(-1, 20)] [InlineData(0, 0)] [InlineData(0, 101)]
    public async Task Pagination_IsBounded(int skip, int take)
    {
        var repo = new AdministrationFakeRepository(); var service = Service(repo);
        await Assert.ThrowsAsync<ArgumentException>(async () => await service.ListTenantsAsync(skip, take, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListClientsAsync(repo.Tenant.Id, skip, take, default));
    }

    [Fact]
    public async Task ValidMetadataOperations_PreserveTenantScopeAndCancellation()
    {
        var repo = new AdministrationFakeRepository(); var service = Service(repo); using var source = new CancellationTokenSource();
        Assert.Single(await service.ListTenantsAsync(0, 20, source.Token));
        Assert.Single(await service.ListClientsAsync(repo.Tenant.Id, 0, 20, source.Token));
        await service.UpdateTenantAsync(repo.Tenant.Id, " Renamed ", "America/Sao_Paulo", false, source.Token);
        Assert.Equal("Renamed", repo.Tenant.Name); Assert.False(repo.Tenant.IsActive);
        await service.SetClientActiveAsync(repo.Tenant.Id, repo.ClientId, false, source.Token);
        Assert.False(repo.ClientActive); Assert.Equal(source.Token, repo.LastToken);
    }

    [Theory]
    [InlineData("")] [InlineData(" ")]
    public async Task BlankNames_AreRejected(string name)
    {
        var repo = new AdministrationFakeRepository();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(repo).UpdateTenantAsync(repo.Tenant.Id, name, "UTC", true, default));
    }

    [Fact]
    public async Task InvalidClientIdentifiersAndLongNames_AreRejected()
    {
        var repo = new AdministrationFakeRepository(); var service = Service(repo);
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateTenantAsync(repo.Tenant.Id, new string('x', 201), "UTC", true, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateClientAsync(repo.Tenant.Id, "not valid", default));
        Assert.Null(repo.CreatedClient);
    }

    [Fact]
    public async Task TenantTimeZone_IsValidatedAndStored()
    {
        var repo = new AdministrationFakeRepository();
        var service = Service(repo);

        await service.UpdateTenantAsync(repo.Tenant.Id, "Company", "America/Sao_Paulo", true, default);

        Assert.Equal("America/Sao_Paulo", repo.Tenant.TimeZoneId);
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateTenantAsync(repo.Tenant.Id, "Company", "Not/AZone", true, default));
    }

    [Fact]
    public async Task ProviderEdit_PreservesSecretsAndResponsesOmitThem()
    {
        var repo = new AdministrationFakeRepository(); var service = Service(repo);
        repo.Configurations.Add(new(repo.Tenant.Id, "Bandwidth", "client", "private-secret", "+15550000001", true, true,
            "{\"accountId\":\"account\",\"applicationId\":\"app\",\"webhookPassword\":\"private-callback\"}"));
        var summaries = await service.ListProvidersAsync(repo.Tenant.Id, default);
        var json = JsonSerializer.Serialize(summaries);
        Assert.DoesNotContain("private-secret", json); Assert.DoesNotContain("private-callback", json);
        Assert.True(summaries[0].HasApiSecret); Assert.Contains("webhookPassword", summaries[0].ConfiguredSecrets);
        await service.SaveProviderAsync(repo.Tenant.Id, "Bandwidth", new("client", "+15550000001", true, true, "", new() { ["applicationId"] = "new-app", ["webhookPassword"] = "" }), default);
        Assert.Equal("private-secret", repo.SavedProvider!.ApiSecret);
        Assert.Contains("private-callback", repo.SavedProvider.Settings);
        Assert.Contains("new-app", repo.SavedProvider.Settings);
        Assert.Equal(repo.Tenant.Id, repo.SavedProvider.TenantId);
    }

    [Fact]
    public async Task ProviderEdit_ValidatesInputBeforeWriting()
    {
        var repo = new AdministrationFakeRepository(); var service = Service(repo);
        var valid = new ProviderEdit("account", "+15550000001", true, true, "secret", null);
        foreach (var invalid in new[] {
            valid with { AccountId = "" }, valid with { AccountId = new string('a', 201) },
            valid with { FromNumber = "123" }, valid with { IsActive = false },
            valid with { ApiSecret = null }, valid with { ApiSecret = new string('á', 300) } })
            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveProviderAsync(repo.Tenant.Id, "Twilio", invalid, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveProviderAsync(repo.Tenant.Id, "Other", valid, default));
        Assert.Null(repo.SavedProvider);
        await service.SaveProviderAsync(repo.Tenant.Id, "Twilio", valid, default);
        Assert.Equal("secret", repo.SavedProvider!.ApiSecret);
        Assert.Equal(2, (await service.GetProviderCatalogAsync(default)).Count);
    }

    [Fact]
    public void ProviderPolicies_ValidateAndMaskSettings()
    {
        var twilio = new TwilioSettingsPolicy();
        Assert.Null(twilio.MergeAndValidate(null, new Dictionary<string, string?>()));
        Assert.Empty(twilio.Describe("anything").Item1);
        Assert.Throws<ArgumentException>(() => twilio.MergeAndValidate(null, new Dictionary<string, string?> { ["unexpected"] = "value" }));
        var mock = new MockSettingsPolicy();
        Assert.Equal("Mock", mock.Definition.Name);
        Assert.Empty(mock.Describe(null).Item1);
        Assert.Empty(mock.Describe(null).Item2);
        Assert.Throws<ArgumentException>(() => mock.MergeAndValidate(null, new Dictionary<string, string?> { ["unexpected"] = "value" }));
        Assert.Equal("existing", mock.MergeAndValidate("existing", new Dictionary<string, string?>()));
        var bandwidth = new BandwidthSettingsPolicy();
        Assert.Throws<ArgumentException>(() => bandwidth.MergeAndValidate(null, new Dictionary<string, string?>()));
        Assert.Throws<ArgumentException>(() => bandwidth.Describe("invalid"));
        Assert.Throws<ArgumentException>(() => bandwidth.MergeAndValidate(null, new Dictionary<string, string?> { ["unexpected"] = "value" }));
        Assert.Empty(bandwidth.Describe(null).Item2);
        var json = bandwidth.MergeAndValidate(null, new Dictionary<string, string?> { ["accountId"] = " account ", ["applicationId"] = "app", ["webhookPassword"] = "password" });
        Assert.Equal("account", bandwidth.Describe(json).Item1["accountId"]);
        Assert.DoesNotContain("webhookPassword", bandwidth.Describe(json).Item1.Keys);
    }

    internal static AdministrationService Service(AdministrationFakeRepository repository) =>
        new(repository, repository, [new TwilioSettingsPolicy(), new BandwidthSettingsPolicy()], new TestProviderCatalogCache(), repository);

    internal sealed class TestProviderCatalogCache : IProviderCatalogCache
    {
        public Task<IReadOnlyList<ProviderDefinition>> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderDefinition>>([
                new TwilioSettingsPolicy().Definition,
                new BandwidthSettingsPolicy().Definition
            ]);
    }
}

internal sealed class AdministrationFakeRepository : IAdministrationRepository, ITenantSmsProviderRepository, ITenantRateLimitRepository
{
    public TenantSummary Tenant { get; set; } = new(Guid.NewGuid(), "Company", "UTC", true, DateTimeOffset.UtcNow);
    public Guid ClientId { get; } = Guid.NewGuid();
    public Guid LastTenant { get; private set; } public Guid LastClient { get; private set; }
    public CancellationToken LastToken { get; private set; } public bool ClientActive { get; private set; } = true;
    public CreateApiClient? CreatedClient { get; private set; }
    public byte[]? RotatedHash { get; private set; } public byte[]? RotatedSalt { get; private set; } public int RotatedIterations { get; private set; }
    public List<TenantSmsProviderConfiguration> Configurations { get; } = [];
    public TenantSmsProviderConfiguration? SavedProvider { get; private set; }
    public Task<IReadOnlyList<TenantSummary>> ListTenantsAsync(int skip, int take, CancellationToken c) => Task.FromResult<IReadOnlyList<TenantSummary>>([Tenant]);
    public Task<TenantSummary?> GetTenantAsync(Guid id, CancellationToken c) => Task.FromResult(id == Tenant.Id ? Tenant : null);
    public Task<bool> UpdateTenantAsync(Guid id, string name, string timeZoneId, bool active, CancellationToken c) { if (id != Tenant.Id) return Task.FromResult(false); Tenant = Tenant with { Name = name, TimeZoneId = timeZoneId, IsActive = active }; return Task.FromResult(true); }
    public Task<IReadOnlyList<ClientSummary>> ListClientsAsync(Guid tenant, int skip, int take, CancellationToken c) => Task.FromResult<IReadOnlyList<ClientSummary>>([new(ClientId, "client", true, DateTimeOffset.UtcNow)]);
    public Task CreateClientAsync(CreateApiClient client, CancellationToken c) { CreatedClient = client; return Task.CompletedTask; }
    public Task<bool> SetClientActiveAsync(Guid tenant, Guid id, bool active, CancellationToken c) { LastTenant = tenant; LastClient = id; LastToken = c; if (tenant != Tenant.Id || id != ClientId) return Task.FromResult(false); ClientActive = active; return Task.FromResult(true); }
    public Task<string?> RotateClientSecretAsync(Guid tenant, Guid id, byte[] hash, byte[] salt, int iterations, CancellationToken c) { LastTenant = tenant; LastClient = id; RotatedHash = hash; RotatedSalt = salt; RotatedIterations = iterations; return Task.FromResult<string?>(tenant == Tenant.Id && id == ClientId ? "client" : null); }
    public Task<IReadOnlyList<TenantSmsProviderConfiguration>> ListProvidersAsync(Guid tenant, CancellationToken c) => Task.FromResult<IReadOnlyList<TenantSmsProviderConfiguration>>(Configurations.Where(x => x.TenantId == tenant).ToArray());
    public Task UpsertAsync(TenantSmsProviderConfiguration configuration, CancellationToken c = default) { SavedProvider = configuration; return Task.CompletedTask; }
    public Task<TenantSmsProviderConfiguration?> GetAsync(Guid tenant, string provider, CancellationToken c = default) => throw new NotSupportedException();
    public Task<TenantSmsProviderConfiguration?> GetDefaultAsync(Guid tenant, CancellationToken c = default) => throw new NotSupportedException();
    public Task<TenantSmsProviderConfiguration?> GetByAccountAndNumberAsync(string provider, string account, string number, CancellationToken c = default) => throw new NotSupportedException();
    public Task<TenantRateLimitSettings> GetAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(new TenantRateLimitSettings(600, 60, 20));
    public Task SaveAsync(Guid tenantId, TenantRateLimitSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

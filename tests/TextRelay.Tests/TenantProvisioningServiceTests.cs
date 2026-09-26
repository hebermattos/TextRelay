using Sms.Application.Auth;
using Sms.Application.Tenants;

namespace Sms.Infrastructure.Tests;

public sealed class TenantProvisioningServiceTests
{
    [Fact]
    public async Task CreateAsync_GeneratesCredentialsAndHashesSecret()
    {
        var provisioner = new FakeProvisioner();
        var service = new TenantProvisioningService(provisioner);
        var result = await service.CreateAsync(" Example Tenant ", " client-one ");

        Assert.Equal("Example Tenant", result.Name);
        Assert.Equal("client-one", result.ClientId);
        Assert.NotEmpty(result.ClientSecret);
        Assert.NotNull(provisioner.Client);
        Assert.Equal(result.TenantId, provisioner.Client!.TenantId);
        Assert.True(ClientSecretHasher.Verify(result.ClientSecret, provisioner.Client.SecretHash, provisioner.Client.SecretSalt, provisioner.Client.SecretIterations));
    }

    [Fact]
    public async Task CreateAsync_GeneratesClientIdWhenNotProvided()
    {
        var service = new TenantProvisioningService(new FakeProvisioner());
        var result = await service.CreateAsync("Tenant");
        Assert.StartsWith("tenant_", result.ClientId);
        Assert.Contains(result.TenantId.ToString("N"), result.ClientId);
    }

    [Fact]
    public async Task CreateAsync_RejectsBlankName()
    {
        var service = new TenantProvisioningService(new FakeProvisioner());
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(" "));
    }

    [Fact]
    public async Task CreateAsync_RejectsLongName()
    {
        var service = new TenantProvisioningService(new FakeProvisioner());
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new string('x', 201)));
    }

    [Theory]
    [InlineData("client id")]
    [InlineData("client.id")]
    [InlineData("client/id")]
    [InlineData("!")]
    public async Task CreateAsync_RejectsInvalidClientId(string clientId)
    {
        var service = new TenantProvisioningService(new FakeProvisioner());
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync("Tenant", clientId));
    }

    [Fact]
    public async Task CreateAsync_RejectsClientIdLongerThanOneHundredCharacters()
    {
        var service = new TenantProvisioningService(new FakeProvisioner());
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync("Tenant", new string('a', 101)));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("ABC-123")]
    [InlineData("tenant_client")]
    public async Task CreateAsync_AcceptsSupportedClientIds(string clientId)
    {
        var result = await new TenantProvisioningService(new FakeProvisioner()).CreateAsync("Tenant", clientId);
        Assert.Equal(clientId, result.ClientId);
    }

    private sealed class FakeProvisioner : ITenantProvisioner
    {
        public CreateApiClient? Client { get; private set; }
        public Task CreateAsync(Guid tenantId, string name, CreateApiClient client, CancellationToken cancellationToken = default)
        {
            Client = client;
            return Task.CompletedTask;
        }
    }
}

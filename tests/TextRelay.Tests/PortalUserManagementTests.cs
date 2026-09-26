using System.Security.Cryptography;
using Sms.Application.Auth;

namespace Sms.Infrastructure.Tests;

public sealed class PortalUserManagementTests
{
    private const string Password = "a-valid-password-123";

    [Fact]
    public async Task PlatformManagementListsCreatesActivatesAndResetsUsers()
    {
        var repository = new PlatformRepository();
        var service = new PortalUserManagementService(repository);
        Assert.Empty(await service.ListPlatformUsersAsync());

        var id = await service.CreatePlatformUserAsync(" alice_1 ", "Alice@Example.com ", Password, "administrator");
        Assert.Equal(id, repository.User!.Id);
        Assert.Equal("alice_1", repository.User.Username);
        Assert.Equal("alice@example.com", repository.User.Email);
        Assert.Null(repository.User.TenantId);
        Assert.Equal("platform", repository.User.Context);
        Assert.Equal(32, repository.User.PasswordHash.Length);
        Assert.True(ClientSecretHasher.Verify(Password, repository.User.PasswordHash, repository.User.PasswordSalt, 600000));

        Assert.Single(await service.ListPlatformUsersAsync());
        await service.SetActiveAsync(id, false);
        Assert.False(repository.Active);
        await service.ResetPasswordAsync(id, "another-valid-password");
        Assert.True(ClientSecretHasher.Verify("another-valid-password", repository.Hash, repository.Salt, 600000));
    }

    [Fact]
    public async Task TenantManagementPreservesTenantScope()
    {
        var repository = new TenantRepository();
        var service = new TenantPortalUserManagementService(repository);
        var tenantId = Guid.NewGuid();
        var id = await service.CreateAsync(tenantId, "tenant_user", "tenant@example.com", Password, "user");

        Assert.Equal(id, repository.User!.Id);
        Assert.Equal(tenantId, repository.User.TenantId);
        Assert.Equal("tenant", repository.User.Context);
        Assert.Equal(tenantId, (await service.ListAsync(tenantId)).Single().TenantId);
        await service.UpdateAsync(tenantId, id, "updated_user", "updated@example.com", "administrator");
        Assert.Equal("updated_user", repository.User.Username);
        Assert.Equal("updated@example.com", repository.User.Email);
        Assert.Equal("administrator", repository.User.Role);
        await service.SetActiveAsync(tenantId, id, false);
        Assert.False(repository.Active);
        await service.ResetPasswordAsync(tenantId, id, "another-valid-password");
        Assert.Equal(tenantId, repository.LastTenantId);
    }

    [Theory]
    [InlineData("", "valid@example.com", Password, "user")]
    [InlineData("ab", "valid@example.com", Password, "user")]
    [InlineData("valid_user", "bad-email", Password, "user")]
    [InlineData("valid_user", "valid@example.com", "short", "user")]
    [InlineData("valid_user", "valid@example.com", Password, "owner")]
    public async Task InvalidPlatformUsersAreRejected(string username, string email, string password, string role)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new PortalUserManagementService(new PlatformRepository())
                .CreatePlatformUserAsync(username, email, password, role));
    }

    [Fact]
    public async Task MissingAccountsRaiseNotFound()
    {
        var platform = new PortalUserManagementService(new PlatformRepository());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => platform.SetActiveAsync(Guid.NewGuid(), true));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => platform.ResetPasswordAsync(Guid.NewGuid(), Password));

        var tenant = new TenantPortalUserManagementService(new TenantRepository());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => tenant.SetActiveAsync(Guid.NewGuid(), Guid.NewGuid(), true));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => tenant.ResetPasswordAsync(Guid.NewGuid(), Guid.NewGuid(), Password));
    }

    private sealed class PlatformRepository : IPortalUserManagementRepository
    {
        public NewPortalUser? User { get; private set; }
        public bool Active { get; private set; } = true;
        public byte[] Hash { get; private set; } = [];
        public byte[] Salt { get; private set; } = [];
        public Task<IReadOnlyList<PortalUserSummary>> ListPlatformUsersAsync(int skip = 0, int take = 20, string? search = null, string? role = null, bool? isActive = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PortalUserSummary>>(User is null ? [] : [Summary(User)]);
        public Task<Guid> CreatePlatformUserAsync(NewPortalUser user, CancellationToken cancellationToken = default) { User = user; return Task.FromResult(user.Id); }
        public Task<bool> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
        { if (User?.Id != id) return Task.FromResult(false); Active = isActive; return Task.FromResult(true); }
        public Task<bool> ResetPasswordAsync(Guid id, byte[] hash, byte[] salt, int iterations, CancellationToken cancellationToken = default)
        { if (User?.Id != id) return Task.FromResult(false); Hash = hash; Salt = salt; return Task.FromResult(true); }
    }

    private sealed class TenantRepository : ITenantPortalUserManagementRepository
    {
        public NewPortalUser? User { get; private set; }
        public bool Active { get; private set; } = true;
        public Guid LastTenantId { get; private set; }
        public Task<IReadOnlyList<PortalUserSummary>> ListAsync(Guid tenantId, int skip = 0, int take = 20, string? search = null, string? role = null, bool? isActive = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PortalUserSummary>>(User is null || User.TenantId != tenantId ? [] : [Summary(User)]);
        public Task<Guid> CreateAsync(NewPortalUser user, CancellationToken cancellationToken = default) { User = user; return Task.FromResult(user.Id); }
        public Task<bool> UpdateAsync(Guid tenantId, Guid id, string username, string email, string role, CancellationToken cancellationToken = default)
        {
            LastTenantId = tenantId;
            if (User?.Id != id || User.TenantId != tenantId) return Task.FromResult(false);
            User = User with { Username = username, Email = email, Role = role };
            return Task.FromResult(true);
        }
        public Task<bool> SetActiveAsync(Guid tenantId, Guid id, bool isActive, CancellationToken cancellationToken = default)
        { LastTenantId = tenantId; if (User?.Id != id || User.TenantId != tenantId) return Task.FromResult(false); Active = isActive; return Task.FromResult(true); }
        public Task<bool> ResetPasswordAsync(Guid tenantId, Guid id, byte[] hash, byte[] salt, int iterations, CancellationToken cancellationToken = default)
        { LastTenantId = tenantId; return Task.FromResult(User?.Id == id && User.TenantId == tenantId); }
    }

    private static PortalUserSummary Summary(NewPortalUser user) =>
        new(user.Id, user.TenantId, user.Username, user.Email, user.Context, user.Role, true, DateTimeOffset.UtcNow);
}

using Sms.Application.Auth;

namespace Sms.Infrastructure.Tests;

public sealed class UserManagementServiceBehaviorTests
{
    private const string Password = "StrongPassword123!";

    [Fact]
    public async Task PlatformUser_CreateNormalizesAndHashesCredentials()
    {
        var repository = new PlatformRepository();
        var service = new PortalUserManagementService(repository);
        var id = await service.CreatePlatformUserAsync(" user.name ", " USER@Example.com ", Password, "administrator");

        Assert.Equal(id, repository.User!.Id);
        Assert.Equal("user.name", repository.User.Username);
        Assert.Equal("user@example.com", repository.User.Email);
        Assert.Equal("platform", repository.User.Context);
        Assert.Equal("administrator", repository.User.Role);
        Assert.Equal(600000, repository.User.PasswordIterations);
        Assert.NotEmpty(repository.User.PasswordHash);
        Assert.NotEmpty(repository.User.PasswordSalt);
    }

    [Theory]
    [InlineData("ab", "user@example.com", Password, "user")]
    [InlineData("bad user", "user@example.com", Password, "user")]
    [InlineData("validuser", "invalid", Password, "user")]
    [InlineData("validuser", "user@example.com", "short", "user")]
    [InlineData("validuser", "user@example.com", Password, "owner")]
    public async Task PlatformUser_CreateRejectsInvalidInput(string username, string email, string password, string role)
    {
        var service = new PortalUserManagementService(new PlatformRepository());
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreatePlatformUserAsync(username, email, password, role));
    }

    [Theory]
    [InlineData("", "user@example.com", Password, "user")]
    [InlineData("validuser", "", Password, "user")]
    [InlineData("validuser", "user@example.com", "", "user")]
    [InlineData("validuser", "user@example.com", Password, "")]
    public async Task PlatformUser_CreateRejectsBlankInput(string username, string email, string password, string role)
    {
        var service = new PortalUserManagementService(new PlatformRepository());
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreatePlatformUserAsync(username, email, password, role));
    }

    [Fact]
    public async Task PlatformUser_CreateRejectsOversizedEmailAndPassword()
    {
        var service = new PortalUserManagementService(new PlatformRepository());
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreatePlatformUserAsync("validuser", $"{new string('a', 310)}@example.com", Password, "user"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreatePlatformUserAsync("validuser", "user@example.com", new string('x', 129), "user"));
    }

    [Fact]
    public async Task PlatformUser_StateAndPasswordThrowWhenMissing()
    {
        var service = new PortalUserManagementService(new PlatformRepository());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetActiveAsync(Guid.NewGuid(), true));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ResetPasswordAsync(Guid.NewGuid(), Password));
    }

    [Fact]
    public async Task PlatformUser_ListStateAndPasswordDelegate()
    {
        var repository = new PlatformRepository { Found = true };
        var service = new PortalUserManagementService(repository);
        Assert.Empty(await service.ListPlatformUsersAsync());
        await service.SetActiveAsync(Guid.NewGuid(), false);
        await service.ResetPasswordAsync(Guid.NewGuid(), Password);
        Assert.True(repository.PasswordReset);
    }

    [Fact]
    public async Task TenantUser_CreateAndUpdateNormalizeFields()
    {
        var repository = new TenantRepository { Found = true };
        var service = new TenantPortalUserManagementService(repository);
        var tenantId = Guid.NewGuid();
        var id = await service.CreateAsync(tenantId, " tenant.user ", " TENANT@Example.com ", Password, "user");

        Assert.Equal(id, repository.User!.Id);
        Assert.Equal(tenantId, repository.User.TenantId);
        Assert.Equal("tenant.user", repository.User.Username);
        Assert.Equal("tenant@example.com", repository.User.Email);
        Assert.Equal("tenant", repository.User.Context);

        await service.UpdateAsync(tenantId, id, " changed.user ", " CHANGED@Example.com ", "administrator");
        Assert.Equal("changed.user", repository.Username);
        Assert.Equal("changed@example.com", repository.Email);
        Assert.Equal("administrator", repository.Role);
    }

    [Theory]
    [InlineData("x", "user@example.com", Password, "user")]
    [InlineData("bad user", "user@example.com", Password, "user")]
    [InlineData("validuser", "invalid", Password, "user")]
    [InlineData("validuser", "user@example.com", "short", "user")]
    [InlineData("validuser", "user@example.com", Password, "owner")]
    public async Task TenantUser_CreateRejectsInvalidInput(string username, string email, string password, string role)
    {
        var service = new TenantPortalUserManagementService(new TenantRepository());
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Guid.NewGuid(), username, email, password, role));
    }

    [Fact]
    public async Task TenantUser_MissingMutationsThrow()
    {
        var service = new TenantPortalUserManagementService(new TenantRepository());
        var tenantId = Guid.NewGuid();
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateAsync(tenantId, id, "valid.user", "user@example.com", "user"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetActiveAsync(tenantId, id, true));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ResetPasswordAsync(tenantId, id, Password));
    }

    [Fact]
    public async Task TenantUser_ListStateAndPasswordDelegate()
    {
        var repository = new TenantRepository { Found = true };
        var service = new TenantPortalUserManagementService(repository);
        var tenantId = Guid.NewGuid();
        Assert.Empty(await service.ListAsync(tenantId));
        await service.SetActiveAsync(tenantId, Guid.NewGuid(), false);
        await service.ResetPasswordAsync(tenantId, Guid.NewGuid(), Password);
        Assert.True(repository.PasswordReset);
    }

    private sealed class PlatformRepository : IPortalUserManagementRepository
    {
        public NewPortalUser? User { get; private set; }
        public bool Found { get; set; }
        public bool PasswordReset { get; private set; }
        public Task<IReadOnlyList<PortalUserSummary>> ListPlatformUsersAsync(int skip = 0, int take = 20, string? search = null, string? role = null, bool? isActive = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PortalUserSummary>>([]);
        public Task<Guid> CreatePlatformUserAsync(NewPortalUser user, CancellationToken cancellationToken = default) { User = user; return Task.FromResult(user.Id); }
        public Task<bool> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default) => Task.FromResult(Found);
        public Task<bool> ResetPasswordAsync(Guid id, byte[] hash, byte[] salt, int iterations, CancellationToken cancellationToken = default) { PasswordReset = true; return Task.FromResult(Found); }
    }

    private sealed class TenantRepository : ITenantPortalUserManagementRepository
    {
        public NewPortalUser? User { get; private set; }
        public bool Found { get; set; }
        public bool PasswordReset { get; private set; }
        public string? Username { get; private set; }
        public string? Email { get; private set; }
        public string? Role { get; private set; }
        public Task<IReadOnlyList<PortalUserSummary>> ListAsync(Guid tenantId, int skip = 0, int take = 20, string? search = null, string? role = null, bool? isActive = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PortalUserSummary>>([]);
        public Task<Guid> CreateAsync(NewPortalUser user, CancellationToken cancellationToken = default) { User = user; return Task.FromResult(user.Id); }
        public Task<bool> UpdateAsync(Guid tenantId, Guid id, string username, string email, string role, CancellationToken cancellationToken = default) { Username = username; Email = email; Role = role; return Task.FromResult(Found); }
        public Task<bool> SetActiveAsync(Guid tenantId, Guid id, bool isActive, CancellationToken cancellationToken = default) => Task.FromResult(Found);
        public Task<bool> ResetPasswordAsync(Guid tenantId, Guid id, byte[] hash, byte[] salt, int iterations, CancellationToken cancellationToken = default) { PasswordReset = true; return Task.FromResult(Found); }
    }
}

using Microsoft.Extensions.Options;
using Sms.Api.Auth;
using Sms.Application.Auth;

namespace Sms.Infrastructure.Tests;

public sealed class RefreshTokenServiceTests
{
    [Fact]
    public async Task Issue_CreatesHashedRotatingSessionAndAccessToken()
    {
        var repository = new RefreshTokens();
        var user = User();
        var service = Service(repository, new Users(user));
        var issued = await service.IssueAsync(user.Id, user.Username, user.TenantId, user.Context, user.Role);

        Assert.False(string.IsNullOrWhiteSpace(issued.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(issued.RefreshToken));
        Assert.Equal(3600, issued.ExpiresIn);
        Assert.NotNull(repository.Created);
        Assert.Equal(user.Id, repository.Created!.UserId);
        Assert.Equal(32, repository.CreatedHash!.Length);
    }

    [Fact]
    public async Task Rotate_RechecksCurrentPortalUserStateForEveryContext()
    {
        var user = User();
        var repository = new RefreshTokens { Rotated = Session(user) };
        var users = new Users(user);
        var service = Service(repository, users);

        users.Account = null;
        Assert.Null(await service.RotateAsync("token"));

        var platformAdmin = User() with { TenantId = null, Context = "platform", Role = "administrator" };
        repository.Rotated = Session(platformAdmin);
        users.Account = platformAdmin;
        Assert.NotNull(await service.RotateAsync("token"));

        users.Account = platformAdmin with { Role = "user" };
        Assert.Null(await service.RotateAsync("token"));
    }

    private static RefreshTokenService Service(RefreshTokens repository, Users users)
    {
        var options = Options.Create(new JwtOptions { Key = "local-test-key-at-least-32-characters", Issuer = "test", Audience = "test", ExpirationMinutes = 60, RefreshExpirationDays = 7 });
        return new RefreshTokenService(repository, new TokenService(options), users, options);
    }

    private static PortalUserAccount User() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "user", "user@example.com", [], [], 600000, "tenant", "user", true);
    private static RefreshTokenSession Session(PortalUserAccount user) =>
        new(Guid.NewGuid(), user.Id, user.Username, user.TenantId, user.Context, user.Role, DateTimeOffset.UtcNow.AddDays(1));

    private sealed class RefreshTokens : IRefreshTokenRepository
    {
        public RefreshTokenSession? Created { get; private set; }
        public byte[]? CreatedHash { get; private set; }
        public RefreshTokenSession? Rotated { get; set; }
        public Task<bool> RevokeAsync(byte[] tokenHash, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task CreateAsync(RefreshTokenSession session, byte[] tokenHash, CancellationToken cancellationToken = default) { Created = session; CreatedHash = tokenHash; return Task.CompletedTask; }
        public Task<RefreshTokenSession?> RotateAsync(byte[] currentTokenHash, byte[] replacementTokenHash, Guid replacementId, DateTimeOffset replacementExpiresAt, CancellationToken cancellationToken = default) => Task.FromResult(Rotated);
    }

    private sealed class Users(PortalUserAccount? account) : IPortalUserRepository
    {
        public PortalUserAccount? Account { get; set; } = account;
        public Task<PortalUserAccount?> GetActiveByUsernameAsync(string username, string context, string? tenantCode, CancellationToken cancellationToken = default) => Task.FromResult(Account);
        public Task<PortalUserAccount?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Account?.Id == id ? Account : null);
    }
}

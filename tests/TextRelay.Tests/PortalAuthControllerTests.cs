using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sms.Api.Auth;
using Sms.Api.Controllers;
using Sms.Application.Auth;

namespace Sms.Infrastructure.Tests;

public sealed class PortalAuthControllerTests
{
    private const string Password = "administrator-password";
    private readonly TokenService tokens = new(Options.Create(new JwtOptions { Key = "local-test-key-at-least-32-characters", Issuer = "test", Audience = "test" }));

    [Theory]
    [InlineData("tenant", "user")]
    [InlineData("platform", "administrator")]
    [InlineData("platform", "user")]
    public async Task ValidPortalUserReceivesContextualToken(string context, string role)
    {
        var user = Account(context, role);
        var users = new Users(user);
        var controller = new PortalAuthController(users, CreateRefreshTokens(users)) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var result = await controller.Token(new(" portal ", Password, context, context == "tenant" ? " tenant-code " : null), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task UnknownCredentialsAreRejected()
    {
        var users = new Users(null);
        var controller = new PortalAuthController(users, CreateRefreshTokens(users)) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<UnauthorizedResult>(await controller.Token(new("user", Password, "platform"), CancellationToken.None));
    }

    private RefreshTokenService CreateRefreshTokens(IPortalUserRepository users) =>
        new(new RefreshTokens(), tokens, users, Options.Create(new JwtOptions { Key = "local-test-key-at-least-32-characters", Issuer = "test", Audience = "test" }));

    private sealed class RefreshTokens : IRefreshTokenRepository
    {
        public Task<bool> RevokeAsync(byte[] tokenHash, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task CreateAsync(RefreshTokenSession session, byte[] tokenHash, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<RefreshTokenSession?> RotateAsync(byte[] currentTokenHash, byte[] replacementTokenHash, Guid replacementId, DateTimeOffset replacementExpiresAt, CancellationToken cancellationToken = default) => Task.FromResult<RefreshTokenSession?>(null);
    }

    private static PortalUserAccount Account(string context, string role)
    {
        var credentials = ClientSecretHasher.Hash(Password);
        return new(Guid.NewGuid(), context == "tenant" ? Guid.NewGuid() : null, "portal", "portal@example.com", credentials.Hash, credentials.Salt, credentials.Iterations, context, role, true);
    }

    private sealed class Users(PortalUserAccount? account) : IPortalUserRepository
    {
        public Task<PortalUserAccount?> GetActiveByUsernameAsync(string username, string context, string? tenantCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(account is not null && account.Context == context && (context != "tenant" || tenantCode == "tenant-code") ? account : null);
        public Task<PortalUserAccount?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(account?.Id == id ? account : null);
    }
}

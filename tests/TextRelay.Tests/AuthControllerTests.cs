using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sms.Api.Auth;
using Sms.Api.Controllers;
using Sms.Application.Auth;

namespace Sms.Infrastructure.Tests;

public sealed class AuthControllerTests
{
    private static TokenService Tokens() => new(Options.Create(new JwtOptions { Issuer="i", Audience="a", Key="01234567890123456789012345678901", ExpirationMinutes=60 }));

    [Fact]
    public async Task Token_ReturnsUnauthorizedForBlankCredentials()
    {
        var controller = new AuthController(Tokens(), new ClientRepository(null));
        Assert.IsType<UnauthorizedResult>(await controller.Token(new TokenRequest("", ""), default));
    }

    [Fact]
    public async Task Token_ReturnsUnauthorizedForUnknownClient()
    {
        var controller = new AuthController(Tokens(), new ClientRepository(null));
        Assert.IsType<UnauthorizedResult>(await controller.Token(new TokenRequest("client", "secret"), default));
    }

    [Fact]
    public async Task Token_ReturnsUnauthorizedForWrongSecret()
    {
        var hashed = ClientSecretHasher.Hash("correct");
        var credential = new ApiClientCredential(Guid.NewGuid(), "client", hashed.Hash, hashed.Salt, hashed.Iterations);
        var controller = new AuthController(Tokens(), new ClientRepository(credential));
        Assert.IsType<UnauthorizedResult>(await controller.Token(new TokenRequest("client", "wrong"), default));
    }

    [Fact]
    public async Task Token_ReturnsBearerTokenForValidCredentials()
    {
        var hashed = ClientSecretHasher.Hash("correct");
        var credential = new ApiClientCredential(Guid.NewGuid(), "client", hashed.Hash, hashed.Salt, hashed.Iterations);
        var result = await new AuthController(Tokens(), new ClientRepository(credential)).Token(new TokenRequest("client", "correct"), default);
        Assert.IsType<OkObjectResult>(result);
    }

    private sealed class ClientRepository(ApiClientCredential? credential) : IApiClientRepository
    {
        public Task<ApiClientCredential?> GetActiveByClientIdAsync(string clientId, CancellationToken cancellationToken = default) => Task.FromResult(credential);
        public Task CreateAsync(CreateApiClient client, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

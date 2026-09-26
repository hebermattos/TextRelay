using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sms.Api.Auth;
using Sms.Application.Auth;
using Sms.Api.Middleware;

namespace Sms.Api.Controllers;

public sealed record TokenRequest(string ClientId, string ClientSecret);

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(TokenService tokenService, IApiClientRepository clients) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [RequestSizeLimit(4096)]
    [HttpPost("token")]
    public async Task<IActionResult> Token([FromBody] TokenRequest request, CancellationToken cancellationToken)
    {
        if (ControllerContext.HttpContext is not null)
            Response.Headers.CacheControl = "no-store";
        if (request.ClientId?.Length > 100 || request.ClientSecret?.Length > 1024) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.ClientSecret)) return Unauthorized();

        var client = await clients.GetActiveByClientIdAsync(request.ClientId, cancellationToken);
        if (client is null || !ClientSecretHasher.Verify(request.ClientSecret, client.SecretHash, client.SecretSalt, client.SecretIterations))
            return Unauthorized();

        var token = tokenService.Create(client.TenantId, client.ClientId);
        if (ControllerContext.HttpContext is not null)
            HttpContext.Items[ClientLoginAuditMiddleware.IdentityKey] = new ClientLoginIdentity(client.TenantId, client.ClientId);

        return Ok(new
        {
            access_token = token,
            token_type = "Bearer"
        });
    }
}

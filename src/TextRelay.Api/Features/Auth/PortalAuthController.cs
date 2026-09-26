using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sms.Api.Auth;
using Sms.Application.Auth;
using Sms.Api.Middleware;

namespace Sms.Api.Controllers;

public sealed record PortalTokenRequest(string Username, string Password, string Context, string? TenantCode = null);
public sealed record RefreshTokenRequest(string? RefreshToken = null);

[ApiController]
[Route("api/v1/portal/auth")]
public sealed class PortalAuthController(
    IPortalUserRepository users,
    RefreshTokenService refreshTokens) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [RequestSizeLimit(4096)]
    [HttpPost("token")]
    public async Task<IActionResult> Token([FromBody] PortalTokenRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (string.IsNullOrWhiteSpace(request.Username)
            || request.Username.Length > 100
            || string.IsNullOrWhiteSpace(request.Password)
            || request.Password.Length > 128
            || request.Context is not (PortalSecurity.TenantContext or PortalSecurity.PlatformContext)
            || (request.Context == PortalSecurity.TenantContext
                && (string.IsNullOrWhiteSpace(request.TenantCode) || request.TenantCode.Length > 100)))
            return Unauthorized();

        var tenantCode = request.Context == PortalSecurity.TenantContext ? request.TenantCode!.Trim() : null;
        var user = await users.GetActiveByUsernameAsync(
            request.Username.Trim(), request.Context, tenantCode, cancellationToken);

        if (user is not null
            && ClientSecretHasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt, user.PasswordIterations))
        {
            HttpContext.Items[PortalLoginAuditMiddleware.IdentityKey] = new PortalLoginIdentity(user.Id, user.TenantId, user.Context);
            var issued = await refreshTokens.IssueAsync(
                user.Id, user.Username, user.TenantId, user.Context, user.Role,
                cancellationToken: cancellationToken);
            SetRefreshCookie(issued.RefreshToken);
            return Ok(ToResponse(issued));
        }

        return Unauthorized();
    }

    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [RequestSizeLimit(2048)]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var raw = Request.Cookies["textrelay_refresh"];
        if (string.IsNullOrWhiteSpace(raw)) return Unauthorized();
        var issued = await refreshTokens.RotateAsync(raw, cancellationToken);
        if (issued is null) { DeleteRefreshCookie(); return Unauthorized(); }
        SetRefreshCookie(issued.RefreshToken);
        return Ok(ToResponse(issued));
    }

    [Authorize(Policy = PortalSecurity.TenantPortalPolicy)]
    [RequestSizeLimit(2048)]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var raw = Request.Cookies["textrelay_refresh"];
        if (!string.IsNullOrWhiteSpace(raw)) await refreshTokens.RevokeAsync(raw, cancellationToken);
        DeleteRefreshCookie();
        return NoContent();
    }

    private void SetRefreshCookie(string token) => Response.Cookies.Append("textrelay_refresh", token, CookieOptions());
    private void DeleteRefreshCookie() => Response.Cookies.Delete("textrelay_refresh", CookieOptions());
    private CookieOptions CookieOptions() => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        MaxAge = TimeSpan.FromDays(7)
    };

    private static object ToResponse(IssuedTokens issued) => new
    {
        access_token = issued.AccessToken,
        token_type = "Bearer",
        expires_in = issued.ExpiresIn
    };
}

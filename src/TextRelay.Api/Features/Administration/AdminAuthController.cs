using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sms.Api.Auth;
using Sms.Application.Auth;

namespace Sms.Api.Controllers;

public sealed record AdminTokenRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Username,
    [Required, StringLength(128, MinimumLength = 1)] string Password);

[ApiController]
[Route("api/v1/admin/auth")]
public sealed class AdminAuthController(
    IPortalUserRepository users,
    RefreshTokenService refreshTokens) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("token")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> Token(AdminTokenRequest request, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 100
            || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length > 128)
            return Unauthorized();

        var account = await users.GetActiveByUsernameAsync(
            request.Username.Trim(), PortalSecurity.PlatformContext, null, cancellationToken);
        if (account is null
            || account.Role != PortalSecurity.AdministratorRole
            || !ClientSecretHasher.Verify(request.Password, account.PasswordHash, account.PasswordSalt, account.PasswordIterations))
            return Unauthorized();

        var issued = await refreshTokens.IssueAsync(
            account.Id, account.Username, null, PortalSecurity.PlatformContext,
            PortalSecurity.AdministratorRole, cancellationToken);
        HttpContext.Items[PortalSecurity.AdministratorLoginIdentityKey] = account.Id.ToString();
        SetRefreshCookie(issued.RefreshToken);
        return Ok(ToResponse(issued));
    }

    [Authorize(Policy = PortalSecurity.AdminPolicy)]
    [HttpPost("logout")]
    [RequestSizeLimit(2048)]
    public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        var raw = Request.Cookies["textrelay_refresh"];
        if (!string.IsNullOrWhiteSpace(raw)) await refreshTokens.RevokeAsync(raw, cancellationToken);
        DeleteRefreshCookie();
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("refresh")]
    [RequestSizeLimit(2048)]
    public async Task<IActionResult> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        var raw = Request.Cookies["textrelay_refresh"];
        if (string.IsNullOrWhiteSpace(raw)) return Unauthorized();
        var issued = await refreshTokens.RotateAsync(raw, cancellationToken);
        if (issued is null) { DeleteRefreshCookie(); return Unauthorized(); }
        SetRefreshCookie(issued.RefreshToken);
        return Ok(ToResponse(issued));
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
    private static object ToResponse(IssuedTokens issued) => new { access_token = issued.AccessToken, token_type = "Bearer", expires_in = issued.ExpiresIn };
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Sms.Api.Auth;

public sealed class TokenService(IOptions<JwtOptions> options)
{
    public string CreatePortalUser(
        Guid userId,
        string username,
        Guid? tenantId,
        string context,
        string role)
    {
        var settings = options.Value;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("portal_username", username),
            new(PortalSecurity.ContextClaim, context),
            new(PortalSecurity.RoleClaim, role)
        };

        if (tenantId is not null)
            claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));

        var token = new JwtSecurityToken(
            settings.Issuer,
            settings.Audience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(settings.ExpirationMinutes),
            signingCredentials: SigningCredentials(settings));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string Create(Guid tenantId, string subject)
    {
        var settings = options.Value;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, subject),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim(PortalSecurity.ContextClaim, PortalSecurity.TenantContext),
            new Claim(PortalSecurity.RoleClaim, PortalSecurity.UserRole)
        };
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims,
            expires: DateTime.UtcNow.AddMinutes(settings.ExpirationMinutes),
            signingCredentials: SigningCredentials(settings));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static SigningCredentials SigningCredentials(JwtOptions settings) =>
        new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)), SecurityAlgorithms.HmacSha256);
}

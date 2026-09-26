using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Sms.Api.Auth;

namespace Sms.Infrastructure.Tests;

public sealed class TokenServiceTests
{
    [Fact]
    public void Create_EmitsTenantAndSubjectClaims()
    {
        var tenantId = Guid.NewGuid();
        var options = Options.Create(new JwtOptions
        {
            Issuer = "issuer", Audience = "audience",
            Key = "01234567890123456789012345678901", ExpirationMinutes = 30
        });
        var token = new TokenService(options).Create(tenantId, "client-one");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("client-one", jwt.Subject);
        Assert.Equal(tenantId.ToString(), jwt.Claims.Single(x => x.Type == "tenant_id").Value);
        Assert.Equal("issuer", jwt.Issuer);
        Assert.Contains("audience", jwt.Audiences);
        Assert.True(jwt.ValidTo > DateTime.UtcNow);
    }
}

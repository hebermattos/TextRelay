using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Sms.Api.Auth;

namespace Sms.Infrastructure.Tests;

public sealed class HttpTenantContextTests
{
    [Fact]
    public void TenantId_ReturnsClaim()
    {
        var tenantId = Guid.NewGuid();
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", tenantId.ToString())])) };
        var accessor = new HttpContextAccessor { HttpContext = context };
        Assert.Equal(tenantId, new HttpTenantContext(accessor).TenantId);
    }

    [Fact]
    public void TenantId_UsesWorkerTenantOutsideHttpRequest()
    {
        var tenantId = Guid.NewGuid();
        var context = new HttpTenantContext(new HttpContextAccessor());
        context.SetTenant(tenantId);
        Assert.Equal(tenantId, context.TenantId);
    }

    [Fact]
    public void TenantId_RejectsMissingClaim()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        Assert.Throws<UnauthorizedAccessException>(() => new HttpTenantContext(accessor).TenantId);
    }

    [Fact]
    public void TenantId_RejectsInvalidClaim()
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", "invalid")])) };
        Assert.Throws<UnauthorizedAccessException>(() => new HttpTenantContext(new HttpContextAccessor { HttpContext = context }).TenantId);
    }
}

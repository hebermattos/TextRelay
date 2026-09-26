using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sms.Api.Auth;
using Sms.Api.Controllers;
using Sms.Api.Filters;
using Sms.Application.Administration;
using Sms.Application.Auth;

namespace Sms.Infrastructure.Tests;

public sealed class PortalSecurityTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, false, false)]
    public async Task Policies_SeparateAdministrativeAndTenantIdentities(bool tenant, bool admin, bool adminAllowed, bool tenantAllowed)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddAuthorization(PortalSecurity.ConfigureAuthorization);
        using var provider = services.BuildServiceProvider();
        var claims = new List<Claim>();
        if (tenant)
            claims.Add(new("tenant_id", Guid.NewGuid().ToString()));

        // A token has exactly one portal context and role. The tenant+admin case below
        // intentionally combines a platform administrator identity with a tenant id
        // to verify that neither policy accepts the malformed cross-context token.
        if (admin)
        {
            claims.Add(new(PortalSecurity.ContextClaim, PortalSecurity.PlatformContext));
            claims.Add(new(PortalSecurity.RoleClaim, PortalSecurity.AdministratorRole));
        }
        else if (tenant)
        {
            claims.Add(new(PortalSecurity.ContextClaim, PortalSecurity.TenantContext));
            claims.Add(new(PortalSecurity.RoleClaim, PortalSecurity.UserRole));
        }
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        Assert.Equal(adminAllowed, (await authorization.AuthorizeAsync(user, null, options.GetPolicy(PortalSecurity.AdminPolicy)!)).Succeeded);
        Assert.Equal(tenantAllowed, (await authorization.AuthorizeAsync(user, null, options.DefaultPolicy)).Succeeded);
    }

    [Fact]
    public async Task Policies_RejectAnonymousClaimsAndMalformedTenantIdentifiers()
    {
        var services = new ServiceCollection().AddLogging(); services.AddAuthorization(PortalSecurity.ConfigureAuthorization);
        using var provider = services.BuildServiceProvider(); var authorization = provider.GetRequiredService<IAuthorizationService>();
        var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(PortalSecurity.ContextClaim, PortalSecurity.PlatformContext)])), null, options.GetPolicy(PortalSecurity.AdminPolicy)!)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", "invalid")], "Bearer")), null, options.DefaultPolicy)).Succeeded);
    }

    [Theory]
    [InlineData(null, "secret", false)] [InlineData("secret", null, false)]
    [InlineData("secret", "wrong", false)] [InlineData("secret", "secret", true)]
    public void Bootstrap_RequiresConfiguredKey(string? expected, string? supplied, bool allowed)
    {
        Assert.Equal(allowed, PortalSecurity.ValidateAdminKey(expected, supplied));
        Assert.False(PortalSecurity.ValidateAdminKey("secret", new string('x', 1025)));
    }

    [Fact]
    public void PlatformAdministratorTokenUsesPortalClaimsAndNoTenantIdentity()
    {
        var id = Guid.NewGuid();
        var token = new JwtSecurityTokenHandler().ReadJwtToken(
            Tokens().CreatePortalUser(id, "admin", null, PortalSecurity.PlatformContext, PortalSecurity.AdministratorRole));
        Assert.Equal(id.ToString(), token.Subject);
        Assert.Contains(token.Claims, x => x.Type == "portal_username" && x.Value == "admin");
        Assert.Contains(token.Claims, x => x.Type == PortalSecurity.ContextClaim && x.Value == PortalSecurity.PlatformContext);
        Assert.Contains(token.Claims, x => x.Type == PortalSecurity.RoleClaim && x.Value == PortalSecurity.AdministratorRole);
        Assert.DoesNotContain(token.Claims, x => x.Type == "tenant_id");
    }

    [Theory]
    [InlineData(true, true)] [InlineData(false, false)]
    public async Task TenantTokens_RecheckActiveCredentials(bool active, bool accepted)
    {
        var tenant = Guid.NewGuid();
        var repository = new Clients(active ? new(tenant, "client", [], [], 100000) : null);
        var context = Context(repository, new("tenant_id", tenant.ToString()), new(ClaimTypes.NameIdentifier, "client"));
        await PortalSecurity.ValidateTenantAsync(context);
        Assert.Equal(accepted, context.Result?.Failure is null);
        Assert.Equal("client", repository.RequestedClient);
    }

    [Fact]
    public async Task TenantTokens_RejectCrossTenantCredentialsOrMissingSubject()
    {
        var repo = new Clients(new(Guid.NewGuid(), "client", [], [], 100000));
        var crossTenant = Context(repo, new("tenant_id", Guid.NewGuid().ToString()), new("sub", "client"));
        await PortalSecurity.ValidateTenantAsync(crossTenant); Assert.NotNull(crossTenant.Result?.Failure);
        var missingSubject = Context(repo, new Claim("tenant_id", Guid.NewGuid().ToString()));
        await PortalSecurity.ValidateTenantAsync(missingSubject); Assert.NotNull(missingSubject.Result?.Failure);
        var malformed = Context(repo, new("tenant_id", "invalid"), new("sub", "client"));
        await PortalSecurity.ValidateTenantAsync(malformed); Assert.NotNull(malformed.Result?.Failure);
        var platformWithoutSubject = Context(repo, new Claim(PortalSecurity.ContextClaim, PortalSecurity.PlatformContext), new Claim(PortalSecurity.RoleClaim, PortalSecurity.AdministratorRole));
        await PortalSecurity.ValidateTenantAsync(platformWithoutSubject); Assert.NotNull(platformWithoutSubject.Result?.Failure);
    }

    [Theory]
    [InlineData(400)] [InlineData(404)] [InlineData(409)] [InlineData(500)]
    public void PortalErrors_DoNotExposeInfrastructureFailures(int status)
    {
        Exception exception = status switch { 400 => new ArgumentException("Invalid input"), 404 => new KeyNotFoundException(), 409 => new PortalUserConflictException(), _ => new InvalidOperationException("private database details") };
        var action = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor(), new ModelStateDictionary());
        var context = new ExceptionContext(action, new List<IFilterMetadata>()) { Exception = exception };
        new PortalExceptionFilter().OnException(context);
        if (status == 500) { Assert.False(context.ExceptionHandled); Assert.Null(context.Result); }
        else { Assert.True(context.ExceptionHandled); Assert.Equal(status, Assert.IsType<ObjectResult>(context.Result).StatusCode); }
    }

    private static TokenService Tokens() => new(Options.Create(new JwtOptions { Issuer = "test", Audience = "test", Key = "local-test-key-with-at-least-32-characters", ExpirationMinutes = 60 }));
    private static TokenValidatedContext Context(Clients clients, params Claim[] claims)
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton<IApiClientRepository>(clients).BuildServiceProvider() };
        return new(http, new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler)), new JwtBearerOptions()) { Principal = new(new ClaimsIdentity(claims, "Bearer")) };
    }
    private sealed class Clients(ApiClientCredential? credential) : IApiClientRepository
    {
        public string? RequestedClient { get; private set; }
        public Task<ApiClientCredential?> GetActiveByClientIdAsync(string id, CancellationToken cancellationToken = default) { RequestedClient = id; return Task.FromResult(credential); }
        public Task CreateAsync(CreateApiClient client, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

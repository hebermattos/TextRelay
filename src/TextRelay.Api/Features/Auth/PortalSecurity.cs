using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Sms.Application.Auth;

namespace Sms.Api.Auth;

public static class PortalSecurity
{
    public const string AdminPolicy = "PlatformAdministrator";
    public const string UserPolicy = "TenantUser";
    public const string PlatformUserPolicy = "PlatformUser";
    public const string TenantAdministratorPolicy = "TenantAdministrator";
    public const string TenantPortalPolicy = "TenantPortal";
    public const string RoleClaim = "role";
    public const string ContextClaim = "context";
    public const string TenantContext = "tenant";
    public const string PlatformContext = "platform";
    public const string UserRole = "user";
    public const string AdministratorRole = "administrator";
    public static readonly object AdministratorLoginIdentityKey = new();

    public static void ConfigureAuthorization(AuthorizationOptions options)
    {
        options.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
            .RequireClaim(ContextClaim, TenantContext).RequireClaim(RoleClaim, UserRole)
            .RequireAssertion(context => Guid.TryParse(context.User.FindFirst("tenant_id")?.Value, out _)).Build();

        options.AddPolicy(UserPolicy, policy => policy.RequireAuthenticatedUser()
            .RequireClaim(ContextClaim, TenantContext).RequireClaim(RoleClaim, UserRole)
            .RequireAssertion(context => Guid.TryParse(context.User.FindFirst("tenant_id")?.Value, out _)));

        options.AddPolicy(TenantAdministratorPolicy, policy => policy.RequireAuthenticatedUser()
            .RequireClaim(ContextClaim, TenantContext).RequireClaim(RoleClaim, AdministratorRole)
            .RequireAssertion(context => Guid.TryParse(context.User.FindFirst("tenant_id")?.Value, out _)));

        options.AddPolicy(TenantPortalPolicy, policy => policy.RequireAuthenticatedUser()
            .RequireClaim(ContextClaim, TenantContext)
            .RequireAssertion(context =>
                (context.User.HasClaim(RoleClaim, UserRole) || context.User.HasClaim(RoleClaim, AdministratorRole))
                && Guid.TryParse(context.User.FindFirst("tenant_id")?.Value, out _)));

        options.AddPolicy(PlatformUserPolicy, policy => policy.RequireAuthenticatedUser()
            .RequireClaim(ContextClaim, PlatformContext).RequireClaim(RoleClaim, UserRole)
            .RequireAssertion(context => !context.User.HasClaim(x => x.Type == "tenant_id")));

        options.AddPolicy(AdminPolicy, policy => policy.RequireAuthenticatedUser()
            .RequireClaim(ContextClaim, PlatformContext)
            .RequireClaim(RoleClaim, AdministratorRole)
            .RequireAssertion(context => !context.User.HasClaim(x => x.Type == "tenant_id")));
    }

    public static bool ValidateAdminKey(string? expected, string? supplied) =>
        !string.IsNullOrWhiteSpace(expected) && !string.IsNullOrWhiteSpace(supplied)
        && supplied.Length <= 1024
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));

    public static async Task ValidateTenantAsync(TokenValidatedContext context)
    {
        var principal = context.Principal!;
        var tenantClaim = principal.FindFirstValue("tenant_id");
        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var portalContext = principal.FindFirstValue(ContextClaim);

        if (portalContext is not null)
        {
            if (!Guid.TryParse(subject, out var portalUserId))
            {
                context.Fail("Invalid portal user identity.");
                return;
            }

            var portalUsers = context.HttpContext.RequestServices.GetRequiredService<IPortalUserRepository>();
            var portalUser = await portalUsers.GetActiveByIdAsync(portalUserId, context.HttpContext.RequestAborted);
            if (portalUser is null
                || !string.Equals(portalUser.Context, portalContext, StringComparison.Ordinal)
                || !string.Equals(portalUser.Role, principal.FindFirstValue(RoleClaim), StringComparison.Ordinal)
                || (portalUser.TenantId?.ToString() ?? null) != tenantClaim)
                context.Fail("Inactive portal user.");
            return;
        }

        if (tenantClaim is null || !Guid.TryParse(tenantClaim, out var tenantId)
            || string.IsNullOrWhiteSpace(subject))
        {
            context.Fail("Invalid tenant identity.");
            return;
        }

        var clients = context.HttpContext.RequestServices.GetRequiredService<IApiClientRepository>();
        var client = await clients.GetActiveByClientIdAsync(subject, context.HttpContext.RequestAborted);
        if (client is null || client.TenantId != tenantId) context.Fail("Inactive tenant or client.");
    }
}

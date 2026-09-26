using System.Security.Claims;
using Sms.Api.Auth;

namespace Sms.Api.Middleware;

public sealed class PortalLoginAuditMiddleware(IUserActivityWriter activities) : IAuditPipelineStep
{
    internal static readonly object IdentityKey = new();

    public async Task AuditAsync(AuditPipelineContext audit)
    {
        var action = audit.Action;
        if (action?.ControllerName != "PortalAuth")
            return;

        if (action.ActionName == "Token")
        {
            await AuditLoginAsync(audit);
            return;
        }

        if (action.ActionName == "Logout")
            await AuditLogoutAsync(audit);
    }

    private async Task AuditLoginAsync(AuditPipelineContext audit)
    {
        var context = audit.HttpContext;
        var identity = context.Items[IdentityKey] as PortalLoginIdentity;
        if (identity is null || identity.Context != PortalSecurity.TenantContext || identity.TenantId is null)
            return;

        var succeeded = !audit.Failed && context.Response.StatusCode is >= 200 and < 300;
        await activities.WriteAsync(new UserActivity(
            identity.TenantId.Value,
            identity.UserId.ToString(),
            UserActivityKind.Action.ToString(),
            "PortalSignedIn",
            "User",
            identity.UserId.ToString(),
            succeeded ? "Signed in to the portal." : "Could not sign in to the portal.",
            succeeded ? "Succeeded" : "Failed"),
            context.RequestAborted);
    }

    private async Task AuditLogoutAsync(AuditPipelineContext audit)
    {
        var context = audit.HttpContext;
        if (context.User.Identity?.IsAuthenticated != true
            || context.User.FindFirstValue(PortalSecurity.ContextClaim) != PortalSecurity.TenantContext
            || !Guid.TryParse(context.User.FindFirstValue("tenant_id"), out var tenantId))
            return;

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        var succeeded = !audit.Failed && context.Response.StatusCode is >= 200 and < 300;
        await activities.WriteAsync(new UserActivity(
            tenantId,
            userId,
            UserActivityKind.Action.ToString(),
            "PortalSignedOut",
            "User",
            userId,
            succeeded ? "Signed out of the portal." : "Could not sign out of the portal.",
            succeeded ? "Succeeded" : "Failed"),
            context.RequestAborted);
    }
}

internal sealed record PortalLoginIdentity(Guid UserId, Guid? TenantId, string Context);

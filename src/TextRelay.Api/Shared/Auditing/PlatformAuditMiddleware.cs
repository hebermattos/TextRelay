using Sms.Api.Auth;
using System.Security.Claims;

namespace Sms.Api.Middleware;

public sealed class PlatformAuditMiddleware(
    IPlatformActivityWriter activities,
    ILogger<PlatformAuditMiddleware> logger) : IAuditPipelineStep
{
    public async Task AuditAsync(AuditPipelineContext audit)
    {
        var action = audit.Action;
        if (action?.ControllerName is not ("Administration" or "AdminTenants" or "AdminAuth" or "SystemLogs"))
            return;

        var context = audit.HttpContext;
        var status = audit.Failed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
        var administrator = context.User.Identity?.IsAuthenticated == true
            && context.User.HasClaim(PortalSecurity.ContextClaim, PortalSecurity.PlatformContext)
            && context.User.HasClaim(PortalSecurity.RoleClaim, PortalSecurity.AdministratorRole)
            && !context.User.HasClaim(claim => claim.Type == "tenant_id");

        var actor = administrator
            ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")
            : null;

        if (status < 400 && action.ControllerName == "AdminAuth" && action.ActionName == "Token")
            actor = context.Items[PortalSecurity.AdministratorLoginIdentityKey] as string;

        if (status < 400 && action.ControllerName == "AdminTenants" && !administrator)
            actor = "bootstrap-key";

        var targetTenant = context.Request.RouteValues["tenantId"]?.ToString();
        var targetClient = context.Request.RouteValues["clientId"]?.ToString();
        var targetAdministrator = context.Request.RouteValues["administratorId"]?.ToString();
        var resourceId = targetTenant ?? targetClient ?? targetAdministrator;
        var resourceType = targetTenant is not null ? "Tenant"
            : targetClient is not null ? "ApiClient"
            : targetAdministrator is not null ? "PlatformAdministrator"
            : null;

        if (status >= 500)
        {
            logger.LogError(
                "Platform operation {Action} failed. HTTP {StatusCode}. {RequestMethod} {RequestPath}. Target tenant {TargetTenantId}, client {TargetClientId}, administrator {TargetAdministratorId}",
                $"{action.ControllerName}.{action.ActionName}",
                status,
                context.Request.Method,
                context.Request.Path.Value,
                targetTenant,
                targetClient,
                targetAdministrator);
            return;
        }

        if (actor is null)
            return;

        var functionalAction = action.ControllerName == "AdminAuth" && action.ActionName == "Token"
            ? "PlatformSignedIn"
            : action.ControllerName == "AdminAuth" && action.ActionName == "Logout"
                ? "PlatformSignedOut"
                : $"{action.ControllerName}.{action.ActionName}";

        var description = functionalAction switch
        {
            "PlatformSignedIn" => "Signed in to the platform.",
            "PlatformSignedOut" => "Signed out of the platform.",
            _ => $"Performed platform action {functionalAction}."
        };

        await activities.WriteAsync(new PlatformActivity(
            actor,
            UserActivityKind.Action.ToString(),
            functionalAction,
            resourceType,
            resourceId,
            description,
            "Succeeded"),
            context.RequestAborted);
    }
}

namespace Sms.Api.Middleware;

public sealed class ClientLoginAuditMiddleware(IUserActivityWriter activities) : IAuditPipelineStep
{
    internal static readonly object IdentityKey = new();

    public async Task AuditAsync(AuditPipelineContext audit)
    {
        var action = audit.Action;
        if (action?.ControllerName != "Auth" || action.ActionName != "Token")
            return;

        var context = audit.HttpContext;
        var status = audit.Failed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
        var identity = context.Items[IdentityKey] as ClientLoginIdentity;
        var succeeded = status >= 200 && status < 300 && identity is not null;

        // Failed authentication has no trusted tenant identity, so it cannot be written
        // to a tenant audit trail. The authentication failure remains technical telemetry.
        if (identity is null)
            return;

        await activities.WriteAsync(new UserActivity(
            identity.TenantId,
            identity.ClientId,
            UserActivityKind.Action.ToString(),
            "ApiSignedIn",
            "ApiClient",
            identity.ClientId,
            succeeded ? "Signed in to the API." : "Could not sign in to the API.",
            succeeded ? "Succeeded" : "Failed"),
            context.RequestAborted);
    }
}

internal sealed record ClientLoginIdentity(Guid TenantId, string ClientId);

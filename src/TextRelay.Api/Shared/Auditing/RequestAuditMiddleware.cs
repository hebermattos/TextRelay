using System.Security.Claims;

namespace Sms.Api.Middleware;

public sealed class RequestAuditMiddleware(IUserActivityWriter activities) : IAuditPipelineStep
{
    public async Task AuditAsync(AuditPipelineContext audit)
    {
        var context = audit.HttpContext;
        var tenantId = context.User.FindFirstValue("tenant_id");
        if (context.User.Identity?.IsAuthenticated != true || !Guid.TryParse(tenantId, out var parsedTenantId))
            return;

        var controller = audit.Action?.ControllerName;
        var action = audit.Action?.ActionName;
        var description = UserActivityMessageFormatter.Format(controller, action);
        if (description is null || controller is null || action is null)
            return;

        var status = audit.Failed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
        var succeeded = status < 400;
        await activities.WriteAsync(new UserActivity(
            parsedTenantId,
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub"),
            description.Kind.ToString(),
            action,
            controller,
            context.Request.RouteValues["id"]?.ToString(),
            succeeded ? description.Success : description.Failure,
            succeeded ? "Succeeded" : "Failed"),
            context.RequestAborted);
    }
}

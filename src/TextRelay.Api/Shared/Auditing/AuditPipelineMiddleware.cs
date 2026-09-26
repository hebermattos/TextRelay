using Microsoft.AspNetCore.Mvc.Controllers;

namespace Sms.Api.Middleware;

public sealed record AuditPipelineContext(
    HttpContext HttpContext,
    ControllerActionDescriptor? Action,
    bool Failed,
    long StartedTimestamp);

public interface IAuditPipelineStep
{
    Task AuditAsync(AuditPipelineContext context);
}

public sealed class AuditPipelineMiddleware(
    RequestDelegate next,
    ILogger<AuditPipelineMiddleware>? logger = null)
{
    public async Task InvokeAsync(HttpContext context, IEnumerable<IAuditPipelineStep> steps)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var failed = false;

        try
        {
            await next(context);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            var auditContext = new AuditPipelineContext(context, action, failed, started);
            foreach (var step in steps)
            {
                try
                {
                    await step.AuditAsync(auditContext);
                }
                catch (Exception exception)
                {
                    logger?.LogError(
                        exception,
                        "Audit step {AuditStep} failed for {RequestMethod} {RequestPath}.",
                        step.GetType().Name,
                        context.Request.Method,
                        context.Request.Path);
                }
            }
        }
    }
}

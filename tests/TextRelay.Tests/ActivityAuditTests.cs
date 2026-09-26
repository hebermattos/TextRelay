using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Sms.Api.Auth;
using Sms.Api.Controllers;
using Sms.Api.Middleware;
using Sms.Application.Common;

namespace Sms.Infrastructure.Tests;

public sealed class ActivityAuditTests
{
    [Fact]
    public async Task PageRecordsExplicitPageActivity()
    {
        var tenantId = Guid.NewGuid();
        var writer = new Recorder();
        var controller = new ActivityController(new TenantContext(tenantId), writer)
        {
            ControllerContext = new ControllerContext { HttpContext = Context("user-1") }
        };

        var result = await controller.Page(new PageActivityRequest("reports"));

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(tenantId, writer.Activity!.TenantId);
        Assert.Equal("user-1", writer.Activity.UserId);
        Assert.Equal("PageView", writer.Activity.ActivityType);
        Assert.Equal("Opened Reports.", writer.Activity.Description);
    }

    [Fact]
    public async Task PageRejectsUnknownPageWithoutAudit()
    {
        var writer = new Recorder();
        var controller = new ActivityController(new TenantContext(Guid.NewGuid()), writer)
        {
            ControllerContext = new ControllerContext { HttpContext = Context("user-1") }
        };

        Assert.IsType<BadRequestObjectResult>(await controller.Page(new PageActivityRequest("unknown")));
        Assert.Null(writer.Activity);
    }


    [Fact]
    public async Task TenantPortalLogoutRecordsAuditActivity()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var writer = new Recorder();
        var context = Context(userId.ToString());
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", userId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim(PortalSecurity.ContextClaim, PortalSecurity.TenantContext)
        ], "test"));
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new ControllerActionDescriptor
            {
                ControllerName = "PortalAuth",
                ActionName = "Logout"
            }), "test"));

        await new AuditPipelineMiddleware(c =>
        {
            c.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        }).InvokeAsync(context, [new PortalLoginAuditMiddleware(writer)]);

        Assert.Equal(tenantId, writer.Activity!.TenantId);
        Assert.Equal(userId.ToString(), writer.Activity.UserId);
        Assert.Equal("PortalSignedOut", writer.Activity.Action);
        Assert.Equal("Succeeded", writer.Activity.Outcome);
    }

    private static DefaultHttpContext Context(string? subject = null)
    {
        var context = new DefaultHttpContext();
        if (subject is not null)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], "test"));
        return context;
    }

    private sealed class Recorder : IUserActivityWriter
    {
        public UserActivity? Activity { get; private set; }
        public Task WriteAsync(UserActivity activity, CancellationToken cancellationToken = default)
        {
            Activity = activity;
            return Task.CompletedTask;
        }
    }

    private sealed class TenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public string TimeZoneId => "UTC";
    }
}

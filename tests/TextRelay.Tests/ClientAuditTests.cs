using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;
using Sms.Api.Auth;
using Sms.Api.Controllers;
using Sms.Api.Middleware;
using Sms.Application.Auth;

namespace Sms.Infrastructure.Tests;

public sealed class ClientAuditTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoginUsesOnlyVerifiedIdentity(bool valid)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(
            new ControllerActionDescriptor { ControllerName = "Auth", ActionName = "Token" }), "login"));
        var hashed = ClientSecretHasher.Hash("correct-secret");
        var credential = new ApiClientCredential(Guid.NewGuid(), "verified-client", hashed.Hash, hashed.Salt, hashed.Iterations);
        var controller = new AuthController(new TokenService(Options.Create(new JwtOptions
            { Issuer = "i", Audience = "a", Key = "01234567890123456789012345678901" })), new Repository(credential))
            { ControllerContext = new ControllerContext { HttpContext = context } };
        var writer = new ActivityRecorder();
        await RunPipelineAsync(context, async c =>
        {
            var result = await controller.Token(new TokenRequest("untrusted-input", valid ? "correct-secret" : "wrong-secret"), default);
            c.Response.StatusCode = result is OkObjectResult ? 200 : 401;
        }, new ClientLoginAuditMiddleware(writer));
        Assert.Equal(valid ? "verified-client" : null, writer.Activity?.UserId);
        Assert.Equal(valid ? credential.TenantId : null, writer.Activity?.TenantId);
        Assert.DoesNotContain("secret", writer.Activity?.Description ?? "");
        Assert.DoesNotContain("untrusted-input", writer.Activity?.Description ?? "");
    }

    [Theory]
    [InlineData("sub")]
    [InlineData(ClaimTypes.NameIdentifier)]
    public async Task HttpAuditUsesValidatedSubject(string subjectClaim)
    {
        var context = new DefaultHttpContext();
        var tenant = Guid.NewGuid();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("tenant_id", tenant.ToString()), new Claim(subjectClaim, "client-1")], "test"));
        context.Request.Headers["ClientId"] = "forged-client";
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new ControllerActionDescriptor { ControllerName = "Messages", ActionName = "Send" }), "activity"));
        var writer = new ActivityRecorder();
        await RunPipelineAsync(context, _ => Task.CompletedTask, new RequestAuditMiddleware(writer));
        Assert.Equal("client-1", writer.Activity?.UserId);
        Assert.Equal(tenant, writer.Activity?.TenantId);
        Assert.DoesNotContain("forged-client", writer.Activity?.Description ?? "");
    }


    [Theory]
    [InlineData("Messages", "Send", 202, "Sent an SMS message.")]
    [InlineData("TenantUsers", "Create", 201, "Created a tenant user.")]
    [InlineData("TenantUsers", "SetState", 204, "Updated a tenant user's status.")]
    [InlineData("TenantUsers", "ResetPassword", 204, "Reset a tenant user's password.")]
    [InlineData("Alerts", "CreateRule", 201, "Created an alert rule.")]
    [InlineData("Alerts", "UpdateRule", 204, "Updated an alert rule.")]
    [InlineData("Alerts", "DeleteRule", 204, "Deleted an alert rule.")]
    [InlineData("Alerts", "MarkRead", 204, "Marked an alert as read.")]
    [InlineData("Alerts", "MarkAllRead", 204, "Marked all alerts as read.")]
    [InlineData("Messages", "Send", 400, "Could not send an SMS message.")]
    public async Task HttpAuditUsesHumanReadableActivity(
        string controller, string action, int status, string expected)
    {
        var context = new DefaultHttpContext();
        var tenant = Guid.NewGuid();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("tenant_id", tenant.ToString()), new Claim("sub", "client-1")], "test"));
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(
            new ControllerActionDescriptor { ControllerName = controller, ActionName = action }), "activity"));
        context.Request.Method = action.StartsWith("Get") || action is "List" or "Rules" or "Sms" ? "GET" : "POST";
        var writer = new ActivityRecorder();

        await RunPipelineAsync(context, c =>
        {
            c.Response.StatusCode = status;
            return Task.CompletedTask;
        }, new RequestAuditMiddleware(writer));

        Assert.Equal(expected, writer.Activity?.Description);
        Assert.Equal(status < 400 ? "Succeeded" : "Failed", writer.Activity?.Outcome);
    }

    [Fact]
    public async Task UnknownGetIsNotRecordedAsUserActivity()
    {
        var context = new DefaultHttpContext();
        var tenant = Guid.NewGuid();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", tenant.ToString()), new Claim("sub", "client-1")], "test"));
        context.Request.Method = "GET";
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new ControllerActionDescriptor { ControllerName = "NewPage", ActionName = "Get" }), "activity"));
        var writer = new ActivityRecorder();
        await RunPipelineAsync(context, _ => Task.CompletedTask, new RequestAuditMiddleware(writer));
        Assert.Null(writer.Activity);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task UnknownRequestIsNotRecordedAsUserActivity(string method)
    {
        var context = new DefaultHttpContext();
        var tenant = Guid.NewGuid();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", tenant.ToString()), new Claim("sub", "client-1")], "test"));
        context.Request.Method = method;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new ControllerActionDescriptor { ControllerName = "NewFeature", ActionName = "Save" }), "activity"));
        var writer = new ActivityRecorder();
        await RunPipelineAsync(context, _ => Task.CompletedTask, new RequestAuditMiddleware(writer));
        Assert.Null(writer.Activity);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(429)]
    public async Task RejectedLoginHasNoTenant(int status)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(
            new ControllerActionDescriptor { ControllerName = "Auth", ActionName = "Token" }), "login"));
        var writer = new ActivityRecorder();
        await RunPipelineAsync(context, c => { c.Response.StatusCode = status; return Task.CompletedTask; }, new ClientLoginAuditMiddleware(writer));
        Assert.Null(writer.Activity);
    }

    private static Task RunPipelineAsync(HttpContext context, RequestDelegate next, params IAuditPipelineStep[] steps) =>
        new AuditPipelineMiddleware(next).InvokeAsync(context, steps);

    private sealed class Repository(ApiClientCredential credential) : IApiClientRepository
    {
        public Task<ApiClientCredential?> GetActiveByClientIdAsync(string clientId, CancellationToken cancellationToken = default) => Task.FromResult<ApiClientCredential?>(credential);
        public Task CreateAsync(CreateApiClient client, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ActivityRecorder : IUserActivityWriter
    {
        public UserActivity? Activity { get; private set; }
        public Task WriteAsync(UserActivity activity, CancellationToken cancellationToken = default) { Activity = activity; return Task.CompletedTask; }
    }

}

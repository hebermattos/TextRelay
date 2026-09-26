using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Logging;
using Moq;
using Sms.Api.Auth;
using Sms.Api.Middleware;

namespace Sms.Infrastructure.Tests;

public sealed class PlatformAuditMiddlewareTests
{
    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public async Task ServerFailedPlatformOperationsAreSystemErrorsWithoutCustomerTenant(int status)
    {
        var context = Context("Administration", "SaveProvider");
        context.Request.Method = "PUT";
        context.Request.Path = "/api/platform/tenants/test/providers";
        var tenant = Guid.NewGuid();
        context.Request.RouteValues["tenantId"] = tenant.ToString();
        var logger = new RecordingLogger();
        await RunPipelineAsync(context, c => { c.Response.StatusCode = status; return Task.CompletedTask; }, new PlatformAuditMiddleware(new PlatformActivities(), logger));
        Assert.Equal(LogLevel.Error, logger.Level);
        Assert.Equal("Administration.SaveProvider", logger.Values["Action"]);
        Assert.Equal(tenant.ToString(), logger.Values["TargetTenantId"]);
        Assert.Equal("PUT", logger.Values["RequestMethod"]);
        Assert.Equal("/api/platform/tenants/test/providers", logger.Values["RequestPath"]);
        Assert.False(logger.Values.ContainsKey("TenantId"));
    }



    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    public async Task ClientErrorsAreNotWrittenAsSystemErrors(int status)
    {
        var logger = new RecordingLogger();

        await RunPipelineAsync(
            Context("Administration", "SaveProvider"),
            c => { c.Response.StatusCode = status; return Task.CompletedTask; },
            new PlatformAuditMiddleware(new PlatformActivities(), logger));

        Assert.Empty(logger.Values);
    }

    [Fact]
    public async Task AuditStepFailureDoesNotPreventFollowingSteps()
    {
        var context = Context("Administration", "SaveProvider");
        var failing = new Mock<IAuditPipelineStep>();
        failing.Setup(x => x.AuditAsync(It.IsAny<AuditPipelineContext>()))
            .ThrowsAsync(new InvalidOperationException("audit failed"));
        var following = new Mock<IAuditPipelineStep>();

        await new AuditPipelineMiddleware(_ => Task.CompletedTask, Mock.Of<ILogger<AuditPipelineMiddleware>>())
            .InvokeAsync(context, [failing.Object, following.Object]);

        following.Verify(x => x.AuditAsync(It.IsAny<AuditPipelineContext>()), Times.Once);
    }

    [Fact]
    public async Task AuditStepFailureDoesNotFailSuccessfulRequest()
    {
        var context = Context("Administration", "SaveProvider");
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        var failing = new Mock<IAuditPipelineStep>();
        failing.Setup(x => x.AuditAsync(It.IsAny<AuditPipelineContext>()))
            .ThrowsAsync(new InvalidOperationException("audit failed"));

        await new AuditPipelineMiddleware(_ => Task.CompletedTask, Mock.Of<ILogger<AuditPipelineMiddleware>>())
            .InvokeAsync(context, [failing.Object]);

        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
    }

    [Fact]
    public async Task AuditStepFailureDoesNotReplaceOriginalRequestException()
    {
        var context = Context("Administration", "SaveProvider");
        var failing = new Mock<IAuditPipelineStep>();
        failing.Setup(x => x.AuditAsync(It.IsAny<AuditPipelineContext>()))
            .ThrowsAsync(new ApplicationException("audit failed"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AuditPipelineMiddleware(_ => throw new InvalidOperationException("request failed"), Mock.Of<ILogger<AuditPipelineMiddleware>>())
                .InvokeAsync(context, [failing.Object]));

        Assert.Equal("request failed", exception.Message);
    }

    [Fact]
    public async Task AuditPipelinePassesFailureStateToEveryStep()
    {
        var context = Context("Administration", "SaveProvider");
        var first = new Mock<IAuditPipelineStep>();
        var second = new Mock<IAuditPipelineStep>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AuditPipelineMiddleware(_ => throw new InvalidOperationException("request failed"), Mock.Of<ILogger<AuditPipelineMiddleware>>())
                .InvokeAsync(context, [first.Object, second.Object]));

        first.Verify(x => x.AuditAsync(It.Is<AuditPipelineContext>(a => a.Failed && a.HttpContext == context)), Times.Once);
        second.Verify(x => x.AuditAsync(It.Is<AuditPipelineContext>(a => a.Failed && a.HttpContext == context)), Times.Once);
    }

    [Fact]
    public async Task AuditPipelinePassesSuccessStateToStep()
    {
        var context = Context("Administration", "SaveProvider");
        var step = new Mock<IAuditPipelineStep>();

        await new AuditPipelineMiddleware(_ => Task.CompletedTask, Mock.Of<ILogger<AuditPipelineMiddleware>>())
            .InvokeAsync(context, [step.Object]);

        step.Verify(x => x.AuditAsync(It.Is<AuditPipelineContext>(a => !a.Failed && a.HttpContext == context)), Times.Once);
    }

    [Fact]
    public async Task AuditStepFailureIsLoggedWithStepAndRequestContext()
    {
        var context = Context("Administration", "SaveProvider");
        context.Request.Method = "PATCH";
        context.Request.Path = "/api/platform/tenants/tenant-1";
        var step = new Mock<IAuditPipelineStep>();
        step.Setup(x => x.AuditAsync(It.IsAny<AuditPipelineContext>()))
            .ThrowsAsync(new InvalidOperationException("audit failed"));
        var logger = new Mock<ILogger<AuditPipelineMiddleware>>();

        await new AuditPipelineMiddleware(_ => Task.CompletedTask, logger.Object)
            .InvokeAsync(context, [step.Object]);

        logger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) =>
                state.ToString()!.Contains("Audit step") &&
                state.ToString()!.Contains("PATCH") &&
                state.ToString()!.Contains("/api/platform/tenants/tenant-1")),
            It.Is<InvalidOperationException>(e => e.Message == "audit failed"),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task PlatformFailureDoesNotWriteActivity()
    {
        var activities = new Mock<IPlatformActivityWriter>();
        var context = Context("Administration", "UpdateTenant");

        await RunPipelineAsync(
            context,
            c => { c.Response.StatusCode = StatusCodes.Status500InternalServerError; return Task.CompletedTask; },
            new PlatformAuditMiddleware(activities.Object, Mock.Of<ILogger<PlatformAuditMiddleware>>()));

        activities.Verify(x => x.WriteAsync(It.IsAny<PlatformActivity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SuccessfulPlatformOperationIsNotWrittenAsSystemLog()
    {
        var logger = new RecordingLogger();
        await RunPipelineAsync(Context("Administration", "SaveProvider"), c => { c.Response.StatusCode = 204; return Task.CompletedTask; }, new PlatformAuditMiddleware(new PlatformActivities(), logger));
        Assert.Empty(logger.Values);
    }

    [Fact]
    public async Task ExceptionIsRecordedAsFailureAndRethrownWithoutItsDetails()
    {
        var logger = new RecordingLogger();
        var step = new PlatformAuditMiddleware(new PlatformActivities(), logger);
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunPipelineAsync(Context("Administration", "UpdateTenant"), _ => throw new InvalidOperationException("secret"), step));
        Assert.Equal(500, logger.Values["StatusCode"]);
        Assert.DoesNotContain("secret", logger.Message);
    }

    [Theory]
    [InlineData("AdminAuth", 200)]
    [InlineData("AdminTenants", 201)]
    public async Task SuccessfulPlatformActionsAreNotTenantOrSystemLogs(string controller, int status)
    {
        var logger = new RecordingLogger();
        await RunPipelineAsync(Context(controller, "Token"), c => { c.Response.StatusCode = status; return Task.CompletedTask; }, new PlatformAuditMiddleware(new PlatformActivities(), logger));
        Assert.Empty(logger.Values);
    }

    [Fact]
    public async Task DoesNotAuditProviderCallbacksAsUserActions()
    {
        var logger = new RecordingLogger();
        await RunPipelineAsync(Context("TwilioWebhooks", "Inbound"), _ => Task.CompletedTask, new PlatformAuditMiddleware(new PlatformActivities(), logger));
        Assert.Empty(logger.Values);
    }

    private static Task RunPipelineAsync(HttpContext context, RequestDelegate next, params IAuditPipelineStep[] steps) =>
        new AuditPipelineMiddleware(next).InvokeAsync(context, steps);

    private static DefaultHttpContext Context(string controller, string action)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new ControllerActionDescriptor { ControllerName = controller, ActionName = action }), "test"));
        return context;
    }

    private sealed class PlatformActivities : IPlatformActivityWriter
    {
        public PlatformActivity? Last { get; private set; }
        public Task WriteAsync(PlatformActivity activity, CancellationToken cancellationToken = default)
        {
            Last = activity;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger : ILogger<PlatformAuditMiddleware>
    {
        public Dictionary<string, object?> Values { get; private set; } = [];
        public LogLevel Level { get; private set; }
        public string Message { get; private set; } = "";
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Level = level;
            Values = ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(x => x.Key, x => x.Value);
            Message = formatter(state, exception);
        }
    }
}

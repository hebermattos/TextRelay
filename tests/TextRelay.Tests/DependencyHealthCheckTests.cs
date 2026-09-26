using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Sms.Api.Health;
using Sms.Infrastructure.Caching;

namespace Sms.Infrastructure.Tests;

public sealed class DependencyHealthCheckTests
{
    [Fact]
    public void AddDependencyHealthChecks_RegistersExpectedChecks()
    {
        var services = new ServiceCollection();
        var configuration = Configuration();

        services.AddDependencyHealthChecks(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        var names = options.Registrations.Select(x => x.Name).ToArray();

        Assert.Contains("postgres.application", names);
        Assert.Contains("postgres.observability", names);
        Assert.Contains("postgres.reporting", names);
        Assert.Contains("redis", names);
        Assert.Contains("rabbitmq", names);
        Assert.Contains("twilio", names);
        Assert.Contains("bandwidth", names);
    }

    [Fact]
    public void AddDependencyHealthChecks_WhenCacheIsDisabled_RegistersDisabledCacheHealthCheck()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Enabled"] = "false"
            })
            .Build();

        services.AddDependencyHealthChecks(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        var registration = options.Registrations.Single(x => x.Name == "redis");

        Assert.IsType<DependencyHealthChecks.DisabledCacheHealthCheck>(registration.Factory(provider));
    }

    [Fact]
    public async Task ApplicationDatabaseHealthCheck_ReturnsUnhealthyWhenPostgresIsUnavailable()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=127.0.0.1;Port=1;Database=sms_api;Username=sms;Password=test;Timeout=1"
        }).Build();
        var check = new DependencyHealthChecks.ApplicationDatabaseHealthCheck(configuration);

        var result = await check.CheckHealthAsync(Context(check, HealthStatus.Unhealthy));

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("PostgreSQL connection failed.", result.Description);
    }

    [Fact]
    public async Task RedisHealthCheck_ReturnsHealthyWhenRedisResponds()
    {
        var check = new DependencyHealthChecks.RedisHealthCheck(new FakeDistributedCache());

        var result = await check.CheckHealthAsync(Context(check, HealthStatus.Degraded));

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("Redis responded.", result.Description);
    }

    [Fact]
    public async Task RedisHealthCheck_ReturnsConfiguredFailureStatusWhenRedisFails()
    {
        var check = new DependencyHealthChecks.RedisHealthCheck(new ThrowingDistributedCache());

        var result = await check.CheckHealthAsync(Context(check, HealthStatus.Degraded));

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal("Redis connection failed.", result.Description);
    }

    [Fact]
    public async Task RedisHealthCheck_ReturnsHealthyWithoutAccessingRedisWhenCacheIsDisabled()
    {
        var check = new DependencyHealthChecks.DisabledCacheHealthCheck();

        var result = await check.CheckHealthAsync(Context(check, HealthStatus.Degraded));

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("Cache is disabled.", result.Description);
    }

    [Fact]
    public async Task TwilioHealthCheck_TreatsNonServerHttpResponseAsReachable()
    {
        var check = new DependencyHealthChecks.TwilioHealthCheck(
            CreateFactory(HttpStatusCode.Unauthorized));

        var result = await check.CheckHealthAsync(Context(check, HealthStatus.Degraded));

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("HTTP 401", result.Description);
    }

    [Fact]
    public async Task TwilioHealthCheck_ReturnsDegradedForServerFailure()
    {
        var check = new DependencyHealthChecks.TwilioHealthCheck(
            CreateFactory(HttpStatusCode.ServiceUnavailable));

        var result = await check.CheckHealthAsync(Context(check, HealthStatus.Degraded));

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("HTTP 503", result.Description);
    }

    [Fact]
    public async Task TwilioHealthCheck_ReturnsDegradedWhenRequestThrows()
    {
        var client = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("https://example.test/") };
        var check = new DependencyHealthChecks.TwilioHealthCheck(new Factory(client));

        var result = await check.CheckHealthAsync(Context(check, HealthStatus.Degraded));

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal("Twilio API is unreachable.", result.Description);
    }

    [Fact]
    public async Task BandwidthHealthCheck_ReturnsHealthyWhenEndpointResponds()
    {
        var check = new DependencyHealthChecks.BandwidthHealthCheck(
            CreateFactory(HttpStatusCode.NotFound));

        var result = await check.CheckHealthAsync(Context(check, HealthStatus.Degraded));

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("Bandwidth API is reachable", result.Description);
    }

    [Fact]
    public async Task RabbitMqHealthCheck_ReturnsHealthyWhenTcpEndpointAcceptsConnections()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMq:Host"] = "127.0.0.1",
                ["RabbitMq:Port"] = port.ToString()
            }).Build();
            var check = new DependencyHealthChecks.RabbitMqHealthCheck(configuration);

            var result = await check.CheckHealthAsync(Context(check, HealthStatus.Unhealthy));

            Assert.Equal(HealthStatus.Healthy, result.Status);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task RabbitMqHealthCheck_ReturnsUnhealthyWhenTcpEndpointIsUnavailable()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "127.0.0.1",
            ["RabbitMq:Port"] = port.ToString()
        }).Build();
        var check = new DependencyHealthChecks.RabbitMqHealthCheck(configuration);

        var result = await check.CheckHealthAsync(Context(check, HealthStatus.Unhealthy));

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("RabbitMQ connection failed.", result.Description);
    }

    [Fact]
    public async Task HealthResponseWriter_ReturnsStructuredJsonWithoutExceptionDetails()
    {
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["redis"] = new(
                HealthStatus.Healthy,
                "Redis responded.",
                TimeSpan.FromMilliseconds(2),
                null,
                new Dictionary<string, object>(),
                ["cache", "internal"])
        };
        var report = new HealthReport(entries, TimeSpan.FromMilliseconds(2));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await HealthResponseWriter.WriteAsync(context, report);

        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        var root = json.RootElement;

        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("checkedAtUtc", out _));
        Assert.True(root.TryGetProperty("durationMs", out _));
        var redis = root.GetProperty("checks").GetProperty("redis");
        Assert.Equal("Healthy", redis.GetProperty("status").GetString());
        Assert.Equal("Redis responded.", redis.GetProperty("description").GetString());
        Assert.Equal(2, redis.GetProperty("tags").GetArrayLength());
        Assert.DoesNotContain("exception", json.RootElement.GetRawText().ToLowerInvariant());
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=sms_api;Username=sms;Password=test",
            ["ConnectionStrings:LogsPostgres"] = "Host=localhost;Database=sms_api_logs;Username=sms;Password=test",
            ["ConnectionStrings:ReportingPostgres"] = "Host=localhost;Database=sms_api_reporting;Username=sms;Password=test"
        }).Build();

    private static HealthCheckContext Context(IHealthCheck check, HealthStatus failureStatus) => new()
    {
        Registration = new HealthCheckRegistration(
            "test",
            _ => check,
            failureStatus,
            Array.Empty<string>())
    };

    private static Factory CreateFactory(HttpStatusCode statusCode) =>
        new(new HttpClient(new Handler(statusCode))
        {
            BaseAddress = new Uri("https://example.test/")
        });

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Handler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Unavailable.");
    }

    private class FakeDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => null;
        public virtual Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed class ThrowingDistributedCache : FakeDistributedCache
    {
        public override Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("Redis unavailable.");
    }
}

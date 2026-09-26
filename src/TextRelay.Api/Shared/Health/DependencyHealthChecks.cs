using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Sms.Infrastructure.Messaging;
using Sms.Infrastructure.Caching;

namespace Sms.Api.Health;

public static class DependencyHealthChecks
{
    private const string TwilioClientName = "Health.Twilio";
    private const string BandwidthClientName = "Health.Bandwidth";

    public static IServiceCollection AddDependencyHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(TwilioClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.twilio.com/");
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddHttpClient(BandwidthClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.bandwidth.com/");
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        var healthChecks = services.AddHealthChecks()
            .AddCheck<ApplicationDatabaseHealthCheck>(
                "postgres.application",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["database", "internal"],
                timeout: TimeSpan.FromSeconds(3))
            .AddCheck<ObservabilityDatabaseHealthCheck>(
                "postgres.observability",
                failureStatus: HealthStatus.Degraded,
                tags: ["database", "internal"],
                timeout: TimeSpan.FromSeconds(3))
            .AddCheck<ReportingDatabaseHealthCheck>(
                "postgres.reporting",
                failureStatus: HealthStatus.Degraded,
                tags: ["database", "internal"],
                timeout: TimeSpan.FromSeconds(3));

        if (CacheConfiguration.IsEnabled(configuration))
        {
            healthChecks.AddCheck<RedisHealthCheck>(
                "redis",
                failureStatus: HealthStatus.Degraded,
                tags: ["cache", "internal"],
                timeout: TimeSpan.FromSeconds(3));
        }
        else
        {
            healthChecks.AddCheck<DisabledCacheHealthCheck>(
                "redis",
                failureStatus: HealthStatus.Degraded,
                tags: ["cache", "internal"],
                timeout: TimeSpan.FromSeconds(3));
        }

        healthChecks
            .AddCheck<RabbitMqHealthCheck>(
                "rabbitmq",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["messaging", "internal"],
                timeout: TimeSpan.FromSeconds(3))
            .AddCheck<TwilioHealthCheck>(
                "twilio",
                failureStatus: HealthStatus.Degraded,
                tags: ["provider", "external"],
                timeout: TimeSpan.FromSeconds(5))
            .AddCheck<BandwidthHealthCheck>(
                "bandwidth",
                failureStatus: HealthStatus.Degraded,
                tags: ["provider", "external"],
                timeout: TimeSpan.FromSeconds(5));

        return services;
    }

    public abstract class PostgresHealthCheck(string connectionString) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);

                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                command.CommandTimeout = 2;
                var result = await command.ExecuteScalarAsync(cancellationToken);

                return Convert.ToInt32(result) == 1
                    ? HealthCheckResult.Healthy("PostgreSQL responded.")
                    : Failure(context, "PostgreSQL returned an unexpected result.");
            }
            catch
            {
                return Failure(context, "PostgreSQL connection failed.");
            }
        }
    }

    public sealed class ApplicationDatabaseHealthCheck(IConfiguration configuration)
        : PostgresHealthCheck(RequiredConnectionString(configuration, "Postgres"));

    public sealed class ObservabilityDatabaseHealthCheck(IConfiguration configuration)
        : PostgresHealthCheck(RequiredConnectionString(configuration, "LogsPostgres"));

    public sealed class ReportingDatabaseHealthCheck(IConfiguration configuration)
        : PostgresHealthCheck(RequiredConnectionString(configuration, "ReportingPostgres"));

    public sealed class RedisHealthCheck(IDistributedCache cache) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await cache.GetAsync("health:redis-probe", cancellationToken);
                return HealthCheckResult.Healthy("Redis responded.");
            }
            catch
            {
                return Failure(context, "Redis connection failed.");
            }
        }
    }

    public sealed class DisabledCacheHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Healthy("Cache is disabled."));
    }

    public sealed class RabbitMqHealthCheck : IHealthCheck
    {
        private readonly string _host;
        private readonly int _port;

        public RabbitMqHealthCheck(IConfiguration configuration)
        {
            var options = RabbitMqAlertOptions.From(configuration);
            _host = options.Host;
            _port = options.Port;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(_host, _port, cancellationToken);
                return client.Connected
                    ? HealthCheckResult.Healthy("RabbitMQ TCP endpoint is reachable.")
                    : Failure(context, "RabbitMQ connection failed.");
            }
            catch
            {
                return Failure(context, "RabbitMQ connection failed.");
            }
        }
    }

    public abstract class ExternalHttpHealthCheck(
        IHttpClientFactory httpClientFactory,
        string clientName,
        string providerName) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var client = httpClientFactory.CreateClient(clientName);
                using var request = new HttpRequestMessage(HttpMethod.Get, "");
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if ((int)response.StatusCode >= 500)
                    return Failure(context, $"{providerName} returned HTTP {(int)response.StatusCode}.");

                return HealthCheckResult.Healthy(
                    $"{providerName} API is reachable (HTTP {(int)response.StatusCode}).");
            }
            catch
            {
                return Failure(context, $"{providerName} API is unreachable.");
            }
        }
    }

    public sealed class TwilioHealthCheck(IHttpClientFactory httpClientFactory)
        : ExternalHttpHealthCheck(httpClientFactory, TwilioClientName, "Twilio");

    public sealed class BandwidthHealthCheck(IHttpClientFactory httpClientFactory)
        : ExternalHttpHealthCheck(httpClientFactory, BandwidthClientName, "Bandwidth");

    private static string RequiredConnectionString(IConfiguration configuration, string name) =>
        configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException($"Connection string '{name}' is not configured.");

    private static HealthCheckResult Failure(HealthCheckContext context, string description) =>
        new(context.Registration.FailureStatus, description);
}

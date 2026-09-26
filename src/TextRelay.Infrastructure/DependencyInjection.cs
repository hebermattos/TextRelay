using MassTransit;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sms.Application.Auth;
using Sms.Application.Alerts;
using Sms.Application.Common;
using Sms.Application.Administration;
using Sms.Application.Messages;
using Sms.Application.Logs;
using Sms.Application.Providers;
using Sms.Application.Reports;
using Sms.Application.Security;
using Sms.Application.Tenants;
using Sms.Infrastructure.Persistence;
using Sms.Infrastructure.Providers;
using Sms.Infrastructure.Security;
using Sms.Infrastructure.Messaging;
using Sms.Application.OptOut;
using Sms.Application.Templates;
using Sms.Infrastructure.Caching;

namespace Sms.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRedisConnection(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Connection string 'Redis' is required.");

        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
        return services;
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, bool registerConsumers = false)
    {
        var retryOptions = configuration.GetSection("SmsRetry").Get<SmsRetryOptions>() ?? new SmsRetryOptions();
        retryOptions.Validate();
        services.AddSingleton(retryOptions);

        if (CacheConfiguration.IsEnabled(configuration))
        {
            services.AddRedisConnection(configuration);
            services.AddOptions<RedisCacheOptions>()
                .Configure<IConnectionMultiplexer>((options, redis) =>
                {
                    options.ConnectionMultiplexerFactory = () => Task.FromResult(redis);
                    options.InstanceName = "sms-api:";
                });
            services.AddSingleton<IDistributedCache, RedisCache>();
        }
        else
        {
            services.AddSingleton<IDistributedCache, DisabledDistributedCacheProxy>();
        }

        services.AddSingleton<SqlConnectionFactory>();
        services.AddSingleton<LogsSqlConnectionFactory>();
        services.AddSingleton<ReportingSqlConnectionFactory>();
        services.AddSingleton<ResilientDistributedCache>();
        services.AddSingleton<TenantConfigurationCache>();
        services.AddSingleton<IProviderCatalogCache, ProviderCatalogCache>();
        services.AddSingleton<ITenantSmsOverviewOutbox, TenantSmsOverviewOutbox>();
        services.AddSingleton<ISmsQueuePublishSource, SmsQueuePublishSource>();
        services.AddSingleton<ITenantSmsOverviewEventPublisher, TenantSmsOverviewEventPublisher>();
        services.AddScoped<ITenantSmsOverviewProjection, TenantSmsOverviewProjection>();
        var rabbitMq = RabbitMqAlertOptions.From(configuration);
        services.AddSingleton(rabbitMq);
        services.AddHttpClient("RabbitMqManagement", client =>
        {
            client.BaseAddress = new Uri($"http://{rabbitMq.ManagementHost}:{rabbitMq.ManagementPort}/");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{rabbitMq.User}:{rabbitMq.Password}")));
        });
        services.AddMassTransit(bus =>
        {
            if (!registerConsumers)
            {
                bus.UsingRabbitMq((_, rabbit) =>
                {
                    rabbit.Host(rabbitMq.Host, (ushort)rabbitMq.Port, rabbitMq.VirtualHost, host =>
                    {
                        host.Username(rabbitMq.User);
                        host.Password(rabbitMq.Password);
                    });
                });
                return;
            }

            bus.AddConsumer<AlertEvaluationConsumer>();
            bus.AddConsumer<AlertRuleEvaluationConsumer>();
            bus.AddConsumer<SmsSendConsumer>();
            bus.AddConsumer<TenantSmsOverviewConsumer>();
            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(rabbitMq.Host, (ushort)rabbitMq.Port, rabbitMq.VirtualHost, host =>
                {
                    host.Username(rabbitMq.User);
                    host.Password(rabbitMq.Password);
                });
                rabbit.ReceiveEndpoint(rabbitMq.Queue, endpoint =>
                {
                    endpoint.SetQuorumQueue(3);
                    endpoint.PrefetchCount = 1;
                    endpoint.ConcurrentMessageLimit = 1;
                    endpoint.UseMessageRetry(retry => retry.Interval(3, TimeSpan.FromSeconds(5)));
                    endpoint.ConfigureConsumer<AlertEvaluationConsumer>(context);
                });
                rabbit.ReceiveEndpoint(rabbitMq.RuleEvaluationQueue, endpoint =>
                {
                    endpoint.SetQuorumQueue(3);
                    endpoint.PrefetchCount = 8;
                    endpoint.ConcurrentMessageLimit = 8;
                    endpoint.UseMessageRetry(retry => retry.Interval(3, TimeSpan.FromSeconds(5)));
                    endpoint.ConfigureConsumer<AlertRuleEvaluationConsumer>(context);
                });
                rabbit.ReceiveEndpoint(rabbitMq.SendQueue, endpoint =>
                {
                    endpoint.SetQuorumQueue(3);
                    endpoint.PrefetchCount = rabbitMq.SendPrefetchCount;
                    endpoint.ConcurrentMessageLimit = rabbitMq.SendConcurrentMessageLimit;
                    endpoint.UseDelayedRedelivery(redelivery =>
                    {
                        redelivery.Handle<TransientSmsProviderException>();
                        redelivery.Intervals(
                            Enumerable.Range(0, retryOptions.MaxAttempts)
                                .Select(attempt => TimeSpan.FromSeconds(
                                    retryOptions.InitialIntervalSeconds * Math.Pow(2, attempt)))
                                .ToArray());
                    });
                    endpoint.ConfigureConsumer<SmsSendConsumer>(context);
                });
                rabbit.ReceiveEndpoint(rabbitMq.ReportingQueue, endpoint =>
                {
                    endpoint.SetQuorumQueue(3);
                    endpoint.PrefetchCount = 1;
                    endpoint.ConcurrentMessageLimit = 1;
                    endpoint.UseMessageRetry(retry => retry.Interval(3, TimeSpan.FromSeconds(5)));
                    endpoint.ConfigureConsumer<TenantSmsOverviewConsumer>(context);
                });
            });
        });
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
        services.AddSingleton<ISmsContentProtector, AesGcmSmsContentProtector>();
        services.AddSingleton<TwilioWebhookValidator>();
        services.AddScoped<BandwidthWebhookParser>();
        services.AddSingleton<ISmsWebhookUrlProvider, ConfiguredSmsWebhookUrlProvider>();
        services.AddScoped<IApiClientRepository, ApiClientRepository>();
        services.AddScoped<IPortalUserRepository, PortalUserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPortalUserManagementRepository, PortalUserManagementRepository>();
        services.AddScoped<ITenantPortalUserManagementRepository, TenantPortalUserManagementRepository>();
        services.AddScoped<IAdministrationRepository, AdministrationRepository>();
        services.AddScoped<ITenantRateLimitRepository, TenantRateLimitRepository>();
        services.AddScoped<ITenantPortalRepository, TenantPortalRepository>();
        services.AddSingleton<IProviderSettingsPolicy, TwilioSettingsPolicy>();
        services.AddSingleton<IProviderSettingsPolicy, BandwidthSettingsPolicy>();
        services.AddSingleton<IProviderSettingsPolicy, MockSettingsPolicy>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<ITenantTimeZoneProvider, TenantTimeZoneProvider>();
        services.AddScoped<ITenantProvisioner, TenantProvisioner>();
        services.AddScoped<ISmsMessageRepository, SmsMessageRepository>();
        services.AddScoped<IOptOutRepository, OptOutRepository>();
        services.AddScoped<IMessageTemplateRepository, MessageTemplateRepository>();
        services.AddScoped<ISmsSendEventPublisher, SmsSendEventPublisher>();
        services.AddScoped<ISmsReportRepository, SmsReportRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<ILogEntryRepository, LogEntryRepository>();
        services.AddScoped<ITenantSmsProviderRepository, TenantSmsProviderRepository>();
        services.AddScoped<ISmsProviderResolver, SmsProviderResolver>();
        services.AddScoped<ITenantAiSettingsRepository, TenantAiSettingsRepository>();
        services.AddHttpClient<IMessageAssistant, OllamaMessageAssistant>(client =>
        {
            client.BaseAddress = new Uri(configuration["Ollama:BaseUrl"] ?? "http://ollama:11434/");
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddScoped<ISmsProvider, MockSmsProvider>();
        services.AddHttpClient<TwilioSmsProvider>(client =>
        {
            client.BaseAddress = new Uri("https://api.twilio.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<ISmsProvider>(sp => sp.GetRequiredService<TwilioSmsProvider>());
        services.AddHttpClient<BandwidthSmsProvider>(client =>
        {
            client.BaseAddress = new Uri("https://messaging.bandwidth.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient("BandwidthOAuth", client =>
        {
            client.BaseAddress = new Uri("https://api.bandwidth.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<ISmsProvider>(sp => sp.GetRequiredService<BandwidthSmsProvider>());
        return services;
    }

    public static IServiceCollection AddInfrastructureWorkers(this IServiceCollection services)
    {
        services.AddHostedService<RabbitMqMonitoringService>();
        services.AddHostedService<AlertEvaluationOutboxPublisher>();
        services.AddHostedService<TenantSmsOverviewOutboxPublisher>();
        services.AddHostedService<SmsQueuePublisherWorker>();
        return services;
    }
}

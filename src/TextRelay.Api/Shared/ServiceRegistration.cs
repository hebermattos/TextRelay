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

namespace Sms.Api.Shared;

public static class ServiceRegistration
{
    public static IServiceCollection AddRedisConnection(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Connection string 'Redis' is required.");

        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
        return services;
    }

    public static IServiceCollection AddTextRelay(this IServiceCollection services, IConfiguration configuration, bool registerConsumers = false)
    {
        services.AddScoped<SendSmsService>();
        services.AddScoped<SendSmsValidator>();
        services.AddScoped<AdministrationService>();
        services.AddScoped<ReceiveSmsWebhookService>();
        services.AddScoped<TenantProvisioningService>();
        services.AddScoped<AlertService>();
        services.AddScoped<AlertRuleFactory>();
        services.AddScoped<OptOutService>();
        services.AddRabbitMqMessaging(configuration, registerConsumers);

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
}

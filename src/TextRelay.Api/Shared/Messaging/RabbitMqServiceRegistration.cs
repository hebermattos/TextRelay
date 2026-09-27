using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sms.Application.Messages;
using Sms.Infrastructure.Messaging;

namespace Sms.Api.Shared;

public static class RabbitMqServiceRegistration
{
    public static IServiceCollection AddRabbitMqMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        bool registerConsumers = false)
    {
        var retryOptions = configuration.GetSection("SmsRetry").Get<SmsRetryOptions>() ?? new SmsRetryOptions();
        retryOptions.Validate();
        services.AddSingleton(retryOptions);

        var rabbitMq = RabbitMqAlertOptions.From(configuration);
        services.AddSingleton(rabbitMq);
        services.AddHttpClient("RabbitMqManagement", client =>
        {
            client.BaseAddress = new Uri($"http://{rabbitMq.ManagementHost}:{rabbitMq.ManagementPort}/");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes($"{rabbitMq.User}:{rabbitMq.Password}")));
        });

        services.AddMassTransit(bus =>
        {
            if (!registerConsumers)
            {
                bus.UsingRabbitMq((_, rabbit) => ConfigureHost(rabbit, rabbitMq));
                return;
            }

            bus.AddConsumer<AlertEvaluationConsumer>();
            bus.AddConsumer<AlertRuleEvaluationConsumer>();
            bus.AddConsumer<SmsSendConsumer>();
            bus.AddConsumer<TenantSmsOverviewConsumer>();
            bus.UsingRabbitMq((context, rabbit) =>
            {
                ConfigureHost(rabbit, rabbitMq);

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

        return services;
    }

    public static IServiceCollection AddWorkerServices(this IServiceCollection services)
    {
        services.AddHostedService<RabbitMqMonitoringService>();
        services.AddHostedService<AlertEvaluationOutboxPublisher>();
        services.AddHostedService<TenantSmsOverviewOutboxPublisher>();
        services.AddHostedService<SmsQueuePublisherWorker>();
        return services;
    }

    private static void ConfigureHost(IRabbitMqBusFactoryConfigurator rabbit, RabbitMqAlertOptions options) =>
        rabbit.Host(options.Host, (ushort)options.Port, options.VirtualHost, host =>
        {
            host.Username(options.User);
            host.Password(options.Password);
        });
}

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Sms.Application.Common;
using Sms.Api.Shared;
using Sms.Infrastructure.Messaging;
using Sms.Infrastructure.Observability;
using Sms.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddSmsLogging();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(TextRelayTelemetry.ActivitySourceName)
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddMeter(RabbitMqMonitoringService.MeterName)
        .AddOtlpExporter());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<WorkerTenantContext>();
builder.Services.AddScoped<IWorkerTenantContext>(services => services.GetRequiredService<WorkerTenantContext>());
builder.Services.AddScoped<ITenantContext>(services => services.GetRequiredService<WorkerTenantContext>());

builder.Services.AddTextRelay(builder.Configuration, registerConsumers: true);
builder.Services.AddWorkerServices();

await builder.Build().RunAsync();

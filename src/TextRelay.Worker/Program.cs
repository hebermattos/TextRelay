using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Sms.Application;
using Sms.Application.Common;
using Sms.Infrastructure;
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

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, registerConsumers: true);
builder.Services.AddInfrastructureWorkers();

await builder.Build().RunAsync();

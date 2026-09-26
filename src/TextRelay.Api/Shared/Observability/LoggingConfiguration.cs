using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Sms.Infrastructure.Observability;

public static class LoggingConfiguration
{
    public static IHostApplicationBuilder AddSmsLogging(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss 'UTC' ";
            options.UseUtcTimestamp = true;
        });

        var connectionString = builder.Configuration.GetConnectionString("LogsPostgres")
            ?? throw new InvalidOperationException("Connection string 'LogsPostgres' is not configured.");

        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.ParseStateValues = true;
            options.AddProcessor(new BatchLogRecordExportProcessor(new PostgresLogExporter(connectionString)));
            options.AddOtlpExporter();
        });

        return builder;
    }
}

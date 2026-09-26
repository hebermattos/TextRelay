using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Logs;
using System.Diagnostics;

namespace Sms.Infrastructure.Observability;

public sealed class PostgresLogExporter(string connectionString) : BaseExporter<LogRecord>
{
    private static readonly string InsertSystemSql = Sms.Infrastructure.Sql.SqlQuery.Load("Observability/PostgresLogExporter.InsertSystemLog.sql");

    public override ExportResult Export(in Batch<LogRecord> batch)
    {
        var hasPersistableRecords = false;
        foreach (var record in batch)
        {
            if (record.LogLevel is not (LogLevel.Error or LogLevel.Critical)) continue;
            hasPersistableRecords = true;
            break;
        }

        if (!hasPersistableRecords) return ExportResult.Success;

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            foreach (var record in batch)
            {
                if (record.LogLevel is not (LogLevel.Error or LogLevel.Critical)) continue;

                var attributes = record.Attributes?.ToDictionary(x => x.Key, x => x.Value);
                var values = new
                {
                    record.Timestamp,
                    TenantId = TryGetTenantId(attributes),
                    Severity = record.LogLevel.ToString(),
                    Category = Limit(record.CategoryName, 256),
                    Message = Limit(GetStoredMessage(record, attributes), 4000),
                    TraceId = record.TraceId == default ? null : record.TraceId.ToHexString(),
                    SpanId = record.SpanId == default ? null : record.SpanId.ToHexString(),
                    Attributes = LogAttributeSanitizer.Serialize(attributes)
                };

                connection.Execute(InsertSystemSql, values, transaction);
            }

            transaction.Commit();
            return ExportResult.Success;
        }
        catch (Exception exception)
        {
            // Do not use ILogger here: that would feed the failed exporter again and recurse.
            // Trace is emitted to the process stderr/console listener and remains visible even
            // when the logs database itself is unavailable.
            Trace.TraceError("Postgres log export failed: {0}: {1}", exception.GetType().Name, exception.Message);
            return ExportResult.Failure;
        }
    }

    private static string GetStoredMessage(
        LogRecord record,
        IReadOnlyDictionary<string, object?>? attributes)
    {
        if (attributes is not null
            && attributes.TryGetValue("{OriginalFormat}", out var template)
            && template is string messageTemplate)
            return messageTemplate;

        return record.Body?.ToString() ?? string.Empty;
    }

    private static Guid? TryGetTenantId(IReadOnlyDictionary<string, object?>? attributes)
    {
        if (attributes is null || !attributes.TryGetValue("TenantId", out var value)) return null;
        return Guid.TryParse(value?.ToString(), out var tenantId) ? tenantId : null;
    }

    private static string Limit(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= maxLength ? value : value[..maxLength];
}

using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Sms.Infrastructure.Persistence;

public sealed class ReportingSqlConnectionFactory
{
    private readonly string _connectionString;

    static ReportingSqlConnectionFactory()
    {
        SqlMapper.AddTypeHandler(PostgresDateTimeOffsetHandler.Instance);
    }

    public ReportingSqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("ReportingPostgres")
            ?? throw new InvalidOperationException("Connection string 'ReportingPostgres' is not configured.");
    }

    public NpgsqlConnection CreateConnection() => new(_connectionString);
}

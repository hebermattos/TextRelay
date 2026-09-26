using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Sms.Infrastructure.Persistence;

public sealed class LogsSqlConnectionFactory
{
    private readonly string _connectionString;

    static LogsSqlConnectionFactory()
    {
        SqlMapper.AddTypeHandler(PostgresDateTimeOffsetHandler.Instance);
    }

    public LogsSqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("LogsPostgres")
            ?? throw new InvalidOperationException("Connection string 'LogsPostgres' is not configured.");
    }

    public NpgsqlConnection CreateConnection() => new(_connectionString);
}

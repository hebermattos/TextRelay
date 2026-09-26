using System.Data;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Sms.Infrastructure.Persistence;

public sealed class SqlConnectionFactory
{
    private readonly string _connectionString;

    static SqlConnectionFactory()
    {
        SqlMapper.AddTypeHandler(PostgresDateTimeOffsetHandler.Instance);
    }

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public NpgsqlConnection CreateNpgsqlConnection() => new(_connectionString);
    public NpgsqlConnection CreateSqlConnection() => CreateNpgsqlConnection();
    public IDbConnection CreateConnection() => CreateNpgsqlConnection();
}

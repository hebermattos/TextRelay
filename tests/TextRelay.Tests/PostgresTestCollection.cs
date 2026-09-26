namespace Sms.Infrastructure.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresTestCollection
{
    public const string Name = "Postgres";
}

using Sms.Infrastructure.Sql;

namespace Sms.Infrastructure.Tests;

public sealed class SqlQueryTests
{
    [Fact]
    public void Load_ReturnsAndCachesEmbeddedSql()
    {
        const string path = "Persistence/SmsMessageRepository.GetByIdAsync.01.sql";

        var first = SqlQuery.Load(path);
        var second = SqlQuery.Load(path);

        Assert.Contains("FROM SmsMessages", first, StringComparison.Ordinal);
        Assert.Same(first, second);
    }

    [Fact]
    public void Load_RejectsUnknownResource()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => SqlQuery.Load("missing.sql"));

        Assert.Contains("Embedded SQL resource", exception.Message, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("Persistence/SmsReportRepository.GetTenantSummaryAsync.01.sql", "ProviderSmsDailyOverview")]
    [InlineData("Persistence/SmsReportRepository.GetPlatformSummaryAsync.02.sql", "ProviderSmsDailyOverview")]
    [InlineData("Persistence/SmsReportRepository.GetUserSummaryAsync.03.sql", "UserSmsOverview")]
    public void Load_ReturnsReportingQueries(string path, string expectedTable)
    {
        var sql = SqlQuery.Load(path);

        Assert.Contains(expectedTable, sql, StringComparison.Ordinal);
    }
}


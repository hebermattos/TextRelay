using Npgsql;
using Sms.Infrastructure.Persistence;

namespace Sms.Infrastructure.Tests;

public sealed class PostgresDateTimeOffsetHandlerTests
{
    private readonly PostgresDateTimeOffsetHandler _handler = new();

    [Fact]
    public void Parse_DateTime_ReturnsUtcDateTimeOffset()
    {
        var value = new DateTime(2026, 9, 19, 12, 30, 0, DateTimeKind.Unspecified);

        var result = _handler.Parse(value);

        Assert.Equal(TimeSpan.Zero, result.Offset);
        Assert.Equal(DateTime.SpecifyKind(value, DateTimeKind.Utc), result.UtcDateTime);
    }

    [Fact]
    public void Parse_DateTimeOffset_NormalizesToUtc()
    {
        var value = new DateTimeOffset(2026, 9, 19, 12, 30, 0, TimeSpan.FromHours(-3));

        var result = _handler.Parse(value);

        Assert.Equal(TimeSpan.Zero, result.Offset);
        Assert.Equal(value.UtcDateTime, result.UtcDateTime);
    }

    [Fact]
    public void SetValue_UsesUtcDateTime()
    {
        var parameter = new NpgsqlParameter();
        var value = new DateTimeOffset(2026, 9, 19, 12, 30, 0, TimeSpan.FromHours(-3));

        _handler.SetValue(parameter, value);

        var stored = Assert.IsType<DateTime>(parameter.Value);
        Assert.Equal(DateTimeKind.Utc, stored.Kind);
        Assert.Equal(value.UtcDateTime, stored);
    }
}

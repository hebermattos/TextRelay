using Sms.Application.Administration;

namespace Sms.Infrastructure.Tests;

public sealed class TimeZoneCatalogTests
{
    [Fact]
    public void Ids_ReturnsAvailableTimeZonesInOrdinalOrder()
    {
        var ids = TimeZoneCatalog.Ids;

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
    }

    [Fact]
    public void IsValid_AcceptsInstalledTimeZone()
    {
        Assert.True(TimeZoneCatalog.IsValid(TimeZoneInfo.Utc.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not/A/Real/TimeZone")]
    public void IsValid_RejectsMissingOrUnknownTimeZone(string? id)
    {
        Assert.False(TimeZoneCatalog.IsValid(id));
    }
}

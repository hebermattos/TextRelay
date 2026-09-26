namespace Sms.Application.Common;

public interface ITenantTimeZoneProvider
{
    Task<TimeZoneInfo> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public static class TenantDateRange
{
    public static (DateTimeOffset? From, DateTimeOffset? To) ToUtc(
        TimeZoneInfo zone, DateTimeOffset? from, DateTimeOffset? to)
    {
        return (ToUtc(zone, from), ToUtc(zone, to));
    }

    private static DateTimeOffset? ToUtc(TimeZoneInfo zone, DateTimeOffset? value)
    {
        if (!value.HasValue) return null;
        var local = DateTime.SpecifyKind(value.Value.DateTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }
}

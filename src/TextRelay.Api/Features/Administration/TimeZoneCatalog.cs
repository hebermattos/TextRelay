namespace Sms.Application.Administration;

public static class TimeZoneCatalog
{
    public static IReadOnlyList<string> Ids => TimeZoneInfo.GetSystemTimeZones()
        .Select(zone => zone.Id)
        .Order(StringComparer.Ordinal)
        .ToArray();

    public static bool IsValid(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            return true;
        }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }
}

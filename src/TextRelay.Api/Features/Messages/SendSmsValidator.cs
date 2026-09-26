using Sms.Application.Auth;
using Sms.Application.Common;

namespace Sms.Application.Messages;

public sealed class SendSmsValidator(
    ITenantContext tenantContext,
    ITenantTimeZoneProvider timeZones,
    IPortalUserRepository users)
{
    public async Task<DateTimeOffset?> ValidateAsync(
        SendSmsRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.To))
            throw new ArgumentException("Destination phone number is required.");
        if (string.IsNullOrWhiteSpace(request.Body))
            throw new ArgumentException("Message body is required.");
        if (request.Body.Length > 4000)
            throw new ArgumentException("Message body cannot exceed 4000 characters.");
        if (request.To.Length > 32)
            throw new ArgumentException("Destination phone number is too long.");
        if (request.From?.Length > 32)
            throw new ArgumentException("Source phone number is too long.");
        if (request.Provider?.Length > 50)
            throw new ArgumentException("Provider name is too long.");

        await ValidateUserAsync(request.UserId, cancellationToken);
        return await ValidateScheduleAsync(request.ScheduledAt, now, cancellationToken);
    }

    private async Task ValidateUserAsync(Guid? userId, CancellationToken cancellationToken)
    {
        if (!userId.HasValue) return;

        var user = await users.GetActiveByIdAsync(userId.Value, cancellationToken);
        if (user is null
            || user.Context != "tenant"
            || user.TenantId != tenantContext.TenantId)
            throw new ArgumentException("UserId must identify an active user in the authenticated tenant.");
    }

    private async Task<DateTimeOffset?> ValidateScheduleAsync(
        DateTime? scheduledAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!scheduledAt.HasValue) return null;
        if (scheduledAt.Value.Kind != DateTimeKind.Unspecified)
            throw new ArgumentException("ScheduledAt must be a local date and time without a UTC offset.");

        var zone = await timeZones.GetAsync(tenantContext.TenantId, cancellationToken);
        var local = scheduledAt.Value;

        if (zone.IsInvalidTime(local))
            throw new ArgumentException("ScheduledAt does not exist in the tenant time zone due to a daylight-saving transition.");
        if (zone.IsAmbiguousTime(local))
            throw new ArgumentException("ScheduledAt is ambiguous in the tenant time zone due to a daylight-saving transition.");

        var utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
        if (utc <= now)
            throw new ArgumentException("ScheduledAt must be in the future in the tenant time zone.");
        if (utc > now.AddYears(1))
            throw new ArgumentException("ScheduledAt cannot be more than one year in the future.");

        return utc;
    }
}

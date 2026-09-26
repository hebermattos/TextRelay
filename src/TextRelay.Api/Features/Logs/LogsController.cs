using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Auth;
using Sms.Application.Common;
using Sms.Application.Logs;

namespace Sms.Api.Controllers;

[ApiController]
[Authorize(Policy = PortalSecurity.TenantPortalPolicy)]
[Route("api/v1/logs")]
public sealed class LogsController(ITenantContext tenantContext, ILogEntryRepository repository, ITenantTimeZoneProvider? timeZones = null) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] DateTimeOffset? cursorTimestamp = null,
        [FromQuery] long? cursorId = null,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        if (cursorTimestamp.HasValue != cursorId.HasValue || cursorId < 1) return BadRequest(new { error = "cursorTimestamp and cursorId must be provided together with a positive cursorId." });
        if (from.HasValue && to.HasValue && from >= to) return BadRequest(new { error = "from must be earlier than to." });

        take = Math.Clamp(take, 1, 200);
        var range = TenantDateRange.ToUtc(timeZones is null ? TimeZoneInfo.Utc : await timeZones.GetAsync(tenantContext.TenantId, cancellationToken), from, to);
        var rows = await repository.GetActivityAsync(tenantContext.TenantId, range.From, range.To, cursorTimestamp.HasValue ? new LogCursor(cursorTimestamp.Value, cursorId!.Value) : null, take, cancellationToken);
        var zone = timeZones is null ? TimeZoneInfo.Utc : await timeZones.GetAsync(tenantContext.TenantId, cancellationToken);
        return Ok(rows.Select(row => row with { Timestamp = TimeZoneInfo.ConvertTime(row.Timestamp, zone) }));
    }
}

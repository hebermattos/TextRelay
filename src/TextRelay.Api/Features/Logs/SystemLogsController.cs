using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Auth;
using Sms.Application.Logs;

namespace Sms.Api.Controllers;

[ApiController]
[Authorize(Policy = PortalSecurity.AdminPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/admin/system-logs")]
public sealed class SystemLogsController(ILogEntryRepository repository) : ControllerBase
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
        return Ok(await repository.GetSystemAsync(from, to, cursorTimestamp.HasValue ? new LogCursor(cursorTimestamp.Value, cursorId!.Value) : null, take, cancellationToken));
    }
}

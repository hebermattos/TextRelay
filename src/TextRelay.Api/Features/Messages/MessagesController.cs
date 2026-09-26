using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Sms.Api.Auth;
using Sms.Application.Common;
using Sms.Application.Messages;

namespace Sms.Api.Controllers;

[ApiController]
[Authorize(Policy = PortalSecurity.TenantPortalPolicy)]
[Route("api/v1/messages")]
public sealed class MessagesController(ITenantContext tenantContext, ISmsMessageRepository repository, SendSmsService sendSmsService, ITenantTimeZoneProvider timeZones) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SendSmsRequest request, CancellationToken cancellationToken)
    {
        var principal = HttpContext?.User;
        if (principal is not null
            && principal.HasClaim(PortalSecurity.ContextClaim, PortalSecurity.TenantContext)
            && Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub"), out var portalUserId))
            request = request with { UserId = portalUserId };

        var result = await sendSmsService.SendAsync(request, cancellationToken);
        if (result.ScheduledAt.HasValue)
            result = result with { ScheduledAt = TimeZoneInfo.ConvertTime(result.ScheduledAt.Value, await Zone(cancellationToken)) };
        return AcceptedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var message = await repository.GetByIdAsync(tenantContext.TenantId, id, cancellationToken);
        return message is null ? NotFound() : Ok(ToResponse(message, await Zone(cancellationToken)));
    }

    [HttpGet("{id:guid}/status-history")]
    public async Task<IActionResult> GetStatusHistory(Guid id, int skip = 0, int take = 20, CancellationToken cancellationToken = default)
    {
        if (skip < 0) return BadRequest("skip must be zero or greater.");
        take = Math.Clamp(take, 1, 200);
        var message = await repository.GetByIdAsync(tenantContext.TenantId, id, cancellationToken);
        if (message is null) return NotFound();

        var zone = await Zone(cancellationToken);
        return Ok((await repository.GetStatusHistoryAsync(tenantContext.TenantId, id, cancellationToken))
            .Skip(skip).Take(take)
            .Select(item => new { item.Id, item.MessageId, item.Status, CreatedAt = TimeZoneInfo.ConvertTime(item.CreatedAt, zone) }));
    }

    [HttpGet]
    public async Task<IActionResult> GetHistory([FromQuery] int skip = 0, [FromQuery] int take = 20, CancellationToken cancellationToken = default)
    {
        if (skip < 0) return BadRequest("skip must be zero or greater.");
        take = Math.Clamp(take, 1, 200);
        var zone = await Zone(cancellationToken);
        return Ok((await repository.GetHistoryAsync(tenantContext.TenantId, skip, take, cancellationToken))
            .Select(item => ToResponse(item, zone)));
    }

    private Task<TimeZoneInfo> Zone(CancellationToken cancellationToken) =>
        timeZones.GetAsync(tenantContext.TenantId, cancellationToken);

    private static object ToResponse(Sms.Domain.Messages.SmsMessage message, TimeZoneInfo zone) => new
    {
        message.Id, message.TenantId, message.UserId, message.From, message.To, message.Body, message.Provider,
        message.ProviderMessageId, message.Direction, message.QueueStatus, message.Status,
        CreatedAt = TimeZoneInfo.ConvertTime(message.CreatedAt, zone),
        ScheduledAt = message.ScheduledAtUtc.HasValue ? (DateTimeOffset?)TimeZoneInfo.ConvertTime(message.ScheduledAtUtc.Value, zone) : null,
        UpdatedAt = message.UpdatedAt.HasValue ? (DateTimeOffset?)TimeZoneInfo.ConvertTime(message.UpdatedAt.Value, zone) : null
    };
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Auth;
using Sms.Application.Alerts;
using Sms.Application.Common;
using Sms.Api.Filters;

namespace Sms.Api.Controllers;

[ApiController]
[Authorize(Policy = PortalSecurity.TenantPortalPolicy)]
[ServiceFilter(typeof(PortalExceptionFilter))]
[Route("api/v1/alerts")]
public sealed class AlertsController(ITenantContext tenant, AlertService alerts) : ControllerBase
{
    [HttpGet("rules")]
    public async Task<IReadOnlyList<AlertRule>> Rules(int skip = 0, int take = 20, CancellationToken cancellationToken = default)
    {
        if (skip < 0) throw new ArgumentException("Invalid pagination.");
        take = Math.Clamp(take, 1, 200);
        return (await alerts.ListRulesAsync(tenant.TenantId, cancellationToken)).Skip(skip).Take(take).ToList();
    }

    [HttpPost("rules")]
    public async Task<IActionResult> CreateRule(SaveAlertRule request, CancellationToken cancellationToken)
    {
        var id = await alerts.CreateRuleAsync(tenant.TenantId, request, cancellationToken);
        return Created($"/api/v1/alerts/rules/{id}", new { id });
    }

    [HttpPut("rules/{id:guid}")]
    public async Task<IActionResult> UpdateRule(Guid id, SaveAlertRule request, CancellationToken cancellationToken)
    {
        await alerts.UpdateRuleAsync(tenant.TenantId, id, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete("rules/{id:guid}")]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken cancellationToken)
    {
        await alerts.DeleteRuleAsync(tenant.TenantId, id, cancellationToken);
        return NoContent();
    }

    [HttpGet]
    public Task<IReadOnlyList<AlertNotification>> List(
        bool unreadOnly = false, int skip = 0, int take = 20, CancellationToken cancellationToken = default) =>
        alerts.ListAlertsAsync(tenant.TenantId, unreadOnly, skip, Math.Clamp(take, 1, 200), cancellationToken);

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        await alerts.MarkReadAsync(tenant.TenantId, id, cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await alerts.MarkAllReadAsync(tenant.TenantId, cancellationToken);
        return NoContent();
    }
}

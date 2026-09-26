using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Auth;
using Sms.Application.Common;
using Sms.Application.Templates;

namespace Sms.Api.Controllers;

[ApiController]
[Authorize(Policy = PortalSecurity.TenantPortalPolicy)]
[Route("api/v1/templates")]
public sealed class MessageTemplatesController(ITenantContext tenant, IMessageTemplateRepository repository, Sms.Application.Administration.ITenantPortalRepository portal) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(int skip = 0, int take = 20, CancellationToken cancellationToken = default)
    {
        if (skip < 0) return BadRequest("skip must be zero or greater.");
        take = Math.Clamp(take, 1, 200);
        return Ok((await repository.ListAsync(tenant.TenantId, skip, take, cancellationToken)).Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var item = await repository.GetAsync(tenant.TenantId, id, cancellationToken);
        return item is null ? NotFound() : Ok(ToResponse(item));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveTemplateRequest request, CancellationToken cancellationToken)
    {
        var error = Validate(request);
        if (error is not null) return BadRequest(error);
        var item = await repository.CreateAsync(tenant.TenantId, request.Name.Trim(), request.Body.Trim(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, ToResponse(item));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveTemplateRequest request, CancellationToken cancellationToken)
    {
        var error = Validate(request);
        if (error is not null) return BadRequest(error);
        var item = await repository.UpdateAsync(tenant.TenantId, id, request.Name.Trim(), request.Body.Trim(), cancellationToken);
        return item is null ? NotFound() : Ok(ToResponse(item));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await repository.DeleteAsync(tenant.TenantId, id, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/render")]
    public async Task<IActionResult> Render(Guid id, [FromBody] RenderTemplateRequest request, CancellationToken cancellationToken)
    {
        var item = await repository.GetAsync(tenant.TenantId, id, cancellationToken);
        if (item is null) return NotFound();
        try
        {
            var overview = await portal.GetOverviewAsync(tenant.TenantId, cancellationToken);
            var system = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["recipientName"] = request.RecipientName ?? string.Empty,
                ["recipientPhone"] = request.RecipientPhone ?? string.Empty,
                ["tenantName"] = overview?.Name ?? string.Empty
            };
            return Ok(new { body = MessageTemplateRenderer.Render(item.Body, request.Variables, system) });
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    private static string? Validate(SaveTemplateRequest request) =>
        string.IsNullOrWhiteSpace(request.Name) ? "Name is required." :
        request.Name.Trim().Length > 120 ? "Name cannot exceed 120 characters." :
        string.IsNullOrWhiteSpace(request.Body) ? "Body is required." :
        request.Body.Length > 4000 ? "Body cannot exceed 4000 characters." : null;

    private static object ToResponse(MessageTemplate item) => new
    {
        item.Id, item.Name, item.Body,
        Variables = MessageTemplateRenderer.Variables(item.Body),
        item.CreatedAt, item.UpdatedAt
    };
}

public sealed record SaveTemplateRequest(string Name, string Body);
public sealed record RenderTemplateRequest(Dictionary<string, string> Variables, string? RecipientName = null, string? RecipientPhone = null);

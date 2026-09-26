using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Tenants;
using Sms.Api.Auth;
using Sms.Api.Filters;

namespace Sms.Api.Controllers;

public sealed record CreateTenantRequest(string Name, string? ClientId);

[ApiController]
[Authorize(Policy = PortalSecurity.AdminPolicy)]
[ServiceFilter(typeof(PortalExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/admin/tenants")]
public sealed class AdminTenantsController(TenantProvisioningService provisioning) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTenantRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "Name is required." });

        var result = await provisioning.CreateAsync(request.Name, request.ClientId, cancellationToken);
        return Created($"/api/v1/admin/tenants/{result.TenantId}", new
        {
            tenant_id = result.TenantId,
            name = result.Name,
            client_id = result.ClientId,
            client_secret = result.ClientSecret,
            warning = "Store client_secret now. It cannot be recovered."
        });
    }
}

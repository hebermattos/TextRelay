using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Features.Auth;
using Sms.Api.Features.Administration;
using Sms.Api.Shared.Tenancy;

namespace Sms.Api.Features.Overview;

[ApiController]
[Authorize(Policy = PortalSecurity.TenantPortalPolicy)]
[Route("api/v1/overview")]
public sealed class OverviewController(ITenantContext tenant, ITenantPortalRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var overview = await repository.GetOverviewAsync(tenant.TenantId, cancellationToken);
        return overview is null ? NotFound() : Ok(overview);
    }
}

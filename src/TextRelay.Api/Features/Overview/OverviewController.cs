using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Auth;
using Sms.Application.Administration;
using Sms.Application.Common;

namespace Sms.Api.Controllers;

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

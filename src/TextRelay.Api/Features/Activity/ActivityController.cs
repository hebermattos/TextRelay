using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Auth;
using Sms.Api.Middleware;
using Sms.Application.Common;

namespace Sms.Api.Controllers;

public sealed record PageActivityRequest(string Page);

[ApiController]
[Authorize(Policy = PortalSecurity.TenantPortalPolicy)]
[Route("api/v1/activity")]
public sealed class ActivityController(
    ITenantContext tenant,
    IUserActivityWriter activities) : ControllerBase
{
    private static readonly IReadOnlyDictionary<string, string> Pages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["overview"] = "Overview",
            ["messages"] = "Messages",
            ["send"] = "Send SMS",
            ["templates"] = "Templates",
            ["users"] = "Users",
            ["reports"] = "Reports",
            ["alerts"] = "Alerts",
            ["opt-outs"] = "Opt-outs",
            ["logs"] = "Activity logs"
        };

    [HttpPost("page")]
    public async Task<IActionResult> Page([FromBody] PageActivityRequest request)
    {
        if (!Pages.TryGetValue(request.Page, out var page))
            return BadRequest(new { error = "Unknown page." });

        await activities.WriteAsync(new UserActivity(
            tenant.TenantId,
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"),
            UserActivityKind.PageView.ToString(),
            "PageOpened",
            "Page",
            request.Page,
            $"Opened {page}.",
            "Succeeded"),
            HttpContext.RequestAborted);

        return NoContent();
    }
}

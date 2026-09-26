using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Features.Auth;
using Sms.Api.Features.Reports;

namespace Sms.Api.Features.Administration;

[ApiController]
[Authorize(Policy = PortalSecurity.AdminPolicy)]
[Route("api/v1/admin/reports")]
public sealed class AdminReportsController(ISmsReportRepository reports) : ControllerBase
{
    [HttpGet("sms")]
    public Task<PlatformSmsReportSummary> Sms(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? provider,
        CancellationToken cancellationToken) =>
        reports.GetPlatformSummaryAsync(new(from, to, NormalizeProvider(provider)), cancellationToken);

    private static string? NormalizeProvider(string? provider) =>
        string.IsNullOrWhiteSpace(provider) ? null : provider.Trim();
}

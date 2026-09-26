using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Features.Auth;
using Sms.Api.Shared.Filters;
using Sms.Api.Shared.Tenancy;
using Sms.Api.Features.OptOuts;

namespace Sms.Api.Features.OptOuts;

[ApiController]
[Authorize(Policy = PortalSecurity.TenantAdministratorPolicy)]
[ServiceFilter(typeof(PortalExceptionFilter))]
[Route("api/v1/opt-outs")]
public sealed class OptOutsController(ITenantContext tenant, OptOutService optOuts) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<BlockedNumber>> List(int skip = 0, int take = 20, CancellationToken cancellationToken = default) =>
        optOuts.ListAsync(tenant.TenantId, skip, take, cancellationToken);

    [HttpPost]
    public async Task<IActionResult> Add(AddBlockedNumber request, CancellationToken cancellationToken)
    {
        await optOuts.AddAsync(tenant.TenantId, request.PhoneNumber, request.Reason, cancellationToken);
        return NoContent();
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import(IReadOnlyList<AddBlockedNumber> rows, CancellationToken cancellationToken)
    {
        await optOuts.ImportAsync(tenant.TenantId, rows, cancellationToken);
        return NoContent();
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var rows = new List<BlockedNumber>();
        while (true)
        {
            var page = await optOuts.ListAsync(tenant.TenantId, rows.Count, 200, cancellationToken);
            rows.AddRange(page);
            if (page.Count < 200) break;
        }
        var csv = new StringBuilder("PhoneNumber,Source,Reason,CreatedAt\r\n");
        foreach (var row in rows)
            csv.Append(Csv(row.PhoneNumber)).Append(',').Append(Csv(row.Source)).Append(',')
                .Append(Csv(row.Reason ?? string.Empty)).Append(',').Append(row.CreatedAt.ToString("O")).Append("\r\n");
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", "sms-opt-outs.csv");
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        return await optOuts.RemoveAsync(tenant.TenantId, id, cancellationToken) ? NoContent() : NotFound();
    }

    private static string Csv(string value)
    {
        if (value.Length > 0 && "=+-@\t\r\n".Contains(value[0])) value = "'" + value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}

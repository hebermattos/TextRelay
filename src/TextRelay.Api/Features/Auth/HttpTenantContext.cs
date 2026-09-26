using System.Security.Claims;
using Sms.Api.Shared.Tenancy;

namespace Sms.Api.Features.Auth;

public sealed class HttpTenantContext(IHttpContextAccessor httpContextAccessor) : IWorkerTenantContext
{
    private Guid? workerTenantId;

    public Guid TenantId
    {
        get
        {
            if (workerTenantId.HasValue) return workerTenantId.Value;
            var value = httpContextAccessor.HttpContext?.User.FindFirstValue("tenant_id");
            return Guid.TryParse(value, out var tenantId)
                ? tenantId
                : throw new UnauthorizedAccessException("A valid tenant_id claim is required.");
        }
    }

    public void SetTenant(Guid tenantId) => workerTenantId = tenantId;
}

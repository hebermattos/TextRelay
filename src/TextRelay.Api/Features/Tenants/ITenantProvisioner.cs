using Sms.Application.Auth;

namespace Sms.Application.Tenants;

public interface ITenantProvisioner
{
    Task CreateAsync(Guid tenantId, string name, CreateApiClient client, CancellationToken cancellationToken = default);
}

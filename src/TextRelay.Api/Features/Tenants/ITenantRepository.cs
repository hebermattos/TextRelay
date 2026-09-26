namespace Sms.Application.Tenants;

public interface ITenantRepository
{
    Task CreateAsync(Guid id, string name, CancellationToken cancellationToken = default);
}

namespace Sms.Application.Common;

public interface ITenantContext
{
    Guid TenantId { get; }
}

public interface IWorkerTenantContext : ITenantContext
{
    void SetTenant(Guid tenantId);
}

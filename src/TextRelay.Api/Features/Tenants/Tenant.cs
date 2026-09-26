namespace Sms.Domain.Tenants;

public sealed class Tenant
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; }
}

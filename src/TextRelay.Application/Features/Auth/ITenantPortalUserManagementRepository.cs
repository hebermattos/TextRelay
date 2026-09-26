namespace Sms.Application.Auth;

public interface ITenantPortalUserManagementRepository
{
    Task<IReadOnlyList<PortalUserSummary>> ListAsync(Guid tenantId, int skip = 0, int take = 20, string? search = null, string? role = null, bool? isActive = null, CancellationToken cancellationToken = default);
    Task<Guid> CreateAsync(NewPortalUser user, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Guid tenantId, Guid id, string username, string email, string role, CancellationToken cancellationToken = default);
    Task<bool> SetActiveAsync(Guid tenantId, Guid id, bool isActive, CancellationToken cancellationToken = default);
    Task<bool> ResetPasswordAsync(Guid tenantId, Guid id, byte[] hash, byte[] salt, int iterations, CancellationToken cancellationToken = default);
}

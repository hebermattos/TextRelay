namespace Sms.Application.Auth;

public sealed record PortalUserSummary(
    Guid Id,
    Guid? TenantId,
    string Username,
    string Email,
    string Context,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record NewPortalUser(
    Guid Id,
    Guid? TenantId,
    string Username,
    string Email,
    byte[] PasswordHash,
    byte[] PasswordSalt,
    int PasswordIterations,
    string Context,
    string Role);

public interface IPortalUserManagementRepository
{
    Task<IReadOnlyList<PortalUserSummary>> ListPlatformUsersAsync(int skip = 0, int take = 20, string? search = null, string? role = null, bool? isActive = null, CancellationToken cancellationToken = default);
    Task<Guid> CreatePlatformUserAsync(NewPortalUser user, CancellationToken cancellationToken = default);
    Task<bool> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
    Task<bool> ResetPasswordAsync(Guid id, byte[] hash, byte[] salt, int iterations, CancellationToken cancellationToken = default);
}

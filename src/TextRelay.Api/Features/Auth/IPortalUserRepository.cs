namespace Sms.Application.Auth;

public sealed record PortalUserAccount(
    Guid Id,
    Guid? TenantId,
    string Username,
    string Email,
    byte[] PasswordHash,
    byte[] PasswordSalt,
    int PasswordIterations,
    string Context,
    string Role,
    bool IsActive);

public interface IPortalUserRepository
{
    Task<PortalUserAccount?> GetActiveByUsernameAsync(
        string username,
        string context,
        string? tenantCode,
        CancellationToken cancellationToken = default);

    Task<PortalUserAccount?> GetActiveByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

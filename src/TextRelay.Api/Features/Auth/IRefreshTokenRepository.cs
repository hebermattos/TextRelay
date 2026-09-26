namespace Sms.Application.Auth;

public sealed record RefreshTokenSession(
    Guid Id,
    Guid UserId,
    string Username,
    Guid? TenantId,
    string Context,
    string Role,
    DateTimeOffset ExpiresAt);

public interface IRefreshTokenRepository
{
    Task CreateAsync(RefreshTokenSession session, byte[] tokenHash, CancellationToken cancellationToken = default);
    Task<bool> RevokeAsync(byte[] tokenHash, CancellationToken cancellationToken = default);
    Task<RefreshTokenSession?> RotateAsync(
        byte[] currentTokenHash,
        byte[] replacementTokenHash,
        Guid replacementId,
        DateTimeOffset replacementExpiresAt,
        CancellationToken cancellationToken = default);
}

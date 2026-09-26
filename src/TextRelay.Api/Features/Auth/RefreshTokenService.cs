using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Sms.Application.Auth;

namespace Sms.Api.Auth;

public sealed record IssuedTokens(string AccessToken, string RefreshToken, int ExpiresIn);

public sealed class RefreshTokenService(
    IRefreshTokenRepository repository,
    TokenService tokens,
    IPortalUserRepository portalUsers,
    IOptions<JwtOptions> options)
{
    public async Task<IssuedTokens> IssueAsync(
        Guid userId, string username, Guid? tenantId, string context, string role,
        CancellationToken cancellationToken = default)
    {
        var raw = CreateToken();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(options.Value.RefreshExpirationDays);
        var session = new RefreshTokenSession(
            Guid.NewGuid(), userId, username, tenantId, context, role, expiresAt);
        await repository.CreateAsync(session, Hash(raw), cancellationToken);
        return Build(session, raw);
    }

    public Task<bool> RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 512) return Task.FromResult(false);
        return repository.RevokeAsync(Hash(refreshToken), cancellationToken);
    }

    public async Task<IssuedTokens?> RotateAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 512) return null;

        var replacement = CreateToken();
        var replacementId = Guid.NewGuid();
        var replacementExpiresAt = DateTimeOffset.UtcNow.AddDays(options.Value.RefreshExpirationDays);
        var session = await repository.RotateAsync(
            Hash(refreshToken), Hash(replacement), replacementId, replacementExpiresAt, cancellationToken);
        if (session is null) return null;

        var user = await portalUsers.GetActiveByIdAsync(session.UserId, cancellationToken);
            if (user is null
                || user.Context != session.Context
                || user.Role != session.Role
                || user.TenantId != session.TenantId) return null;

        return Build(session with { Id = replacementId, ExpiresAt = replacementExpiresAt }, replacement);
    }

    private IssuedTokens Build(RefreshTokenSession session, string refreshToken)
    {
        var accessToken = tokens.CreatePortalUser(session.UserId, session.Username, session.TenantId, session.Context, session.Role);
        return new IssuedTokens(accessToken, refreshToken, options.Value.ExpirationMinutes * 60);
    }

    private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    private static byte[] Hash(string token) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
}

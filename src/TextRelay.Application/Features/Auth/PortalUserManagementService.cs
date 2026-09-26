using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Net.Mail;

namespace Sms.Application.Auth;

public sealed class PortalUserConflictException : Exception { }

public sealed class PortalUserManagementService(
    IPortalUserManagementRepository repository)
{
    public Task<IReadOnlyList<PortalUserSummary>> ListPlatformUsersAsync(
        int skip = 0, int take = 20, string? search = null, string? role = null, bool? isActive = null,
        CancellationToken cancellationToken = default) =>
        repository.ListPlatformUsersAsync(skip, take, search, role, isActive, cancellationToken);

    public async Task<Guid> CreatePlatformUserAsync(
        string username,
        string email,
        string password,
        string role,
        CancellationToken cancellationToken = default)
    {
        ValidateUsername(username);
        ValidateEmail(email);
        ValidatePassword(password);
        ValidateRole(role);

        var (hash, salt) = HashPassword(password);
        return await repository.CreatePlatformUserAsync(new(
            Guid.NewGuid(), null, username.Trim(), email.Trim().ToLowerInvariant(), hash, salt, 600_000,
            "platform", role), cancellationToken);
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!await repository.SetActiveAsync(id, isActive, cancellationToken))
            throw new KeyNotFoundException();
    }

    public async Task ResetPasswordAsync(Guid id, string password, CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        var (hash, salt) = HashPassword(password);
        if (!await repository.ResetPasswordAsync(id, hash, salt, 600_000, cancellationToken))
            throw new KeyNotFoundException();
    }

    private static void ValidateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username)
            || !Regex.IsMatch(username.Trim(), @"\A[a-zA-Z0-9][a-zA-Z0-9._-]{2,99}\z"))
            throw new ArgumentException("Username must contain 3–100 letters, digits, dots, underscores, or hyphens.");
    }

    private static void ValidateEmail(string email)
    {
        try { _ = new MailAddress(email); }
        catch { throw new ArgumentException("Enter a valid email address."); }
        if (email.Length > 320) throw new ArgumentException("Email must contain up to 320 characters.");
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length is < 15 or > 128)
            throw new ArgumentException("Passwords must contain 15–128 characters.");
    }

    private static void ValidateRole(string role)
    {
        if (role is not ("user" or "administrator"))
            throw new ArgumentException("Role must be user or administrator.");
    }

    private static (byte[] Hash, byte[] Salt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        return (Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000,
            HashAlgorithmName.SHA256, 32), salt);
    }
}

using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Sms.Application.Auth;

namespace Sms.Application.Tenants;

public sealed record ProvisionedTenant(Guid TenantId, string Name, string ClientId, string ClientSecret);

public sealed class TenantProvisioningService(ITenantProvisioner provisioner)
{
    public async Task<ProvisionedTenant> CreateAsync(
        string name,
        string? requestedClientId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalizedName = name.Trim();
        if (normalizedName.Length > 200)
            throw new ArgumentException("Name must be 200 characters or fewer.");

        var tenantId = Guid.NewGuid();
        var clientId = string.IsNullOrWhiteSpace(requestedClientId)
            ? $"tenant_{tenantId:N}"
            : requestedClientId.Trim();

        if (!Regex.IsMatch(clientId, "^[a-zA-Z0-9_-]{1,100}$"))
            throw new ArgumentException("The identifier must contain up to 100 letters, numbers, hyphens, or underscores.");

        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var hashedSecret = ClientSecretHasher.Hash(secret);

        await provisioner.CreateAsync(
            tenantId,
            normalizedName,
            new CreateApiClient(
                tenantId,
                clientId,
                hashedSecret.Hash,
                hashedSecret.Salt,
                hashedSecret.Iterations),
            cancellationToken);

        return new ProvisionedTenant(tenantId, normalizedName, clientId, secret);
    }
}

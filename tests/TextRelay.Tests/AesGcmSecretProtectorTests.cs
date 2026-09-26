using Microsoft.Extensions.Configuration;
using Sms.Infrastructure.Security;

namespace Sms.Infrastructure.Tests;

public sealed class AesGcmSecretProtectorTests
{
    private static AesGcmSecretProtector Create()
    {
        var key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray());
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:MasterKey"] = key
        }).Build();
        return new AesGcmSecretProtector(configuration);
    }

    [Fact]
    public void ProtectAndUnprotect_RoundTripsSecret()
    {
        var protector = Create();
        const string secret = "twilio-auth-token";
        var encrypted = protector.Protect(secret);
        Assert.NotEqual(secret, encrypted);
        Assert.Equal(secret, protector.Unprotect(encrypted));
    }

    [Fact]
    public void Protect_UsesRandomNonce()
    {
        var protector = Create();
        var first = protector.Protect("same-secret");
        var second = protector.Protect("same-secret");
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Unprotect_RejectsTamperedCiphertext()
    {
        var protector = Create();
        var bytes = Convert.FromBase64String(protector.Protect("secret"));
        bytes[^1] ^= 1;
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => protector.Unprotect(Convert.ToBase64String(bytes)));
    }
}

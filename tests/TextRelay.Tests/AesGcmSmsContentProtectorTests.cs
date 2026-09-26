using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Sms.Infrastructure.Security;

namespace Sms.Infrastructure.Tests;

public sealed class AesGcmSmsContentProtectorTests
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:MasterKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }).Build();

    [Theory]
    [InlineData("Body", "hello")]
    [InlineData("Body", "")]
    [InlineData("From", "+15550000001")]
    public void Protect_RoundTripsContent(string field, string plaintext)
    {
        var tenantId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var protector = new AesGcmSmsContentProtector(Configuration);

        var encrypted = protector.Protect(tenantId, messageId, field, plaintext);

        Assert.NotEqual(plaintext, encrypted);
        Assert.Equal(plaintext, protector.Unprotect(tenantId, messageId, field, encrypted));
    }

    [Fact]
    public void Unprotect_RejectsAnotherTenantMessageOrField()
    {
        var tenantId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var protector = new AesGcmSmsContentProtector(Configuration);
        var encrypted = protector.Protect(tenantId, messageId, "Body", "private text");

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(Guid.NewGuid(), messageId, "Body", encrypted));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tenantId, Guid.NewGuid(), "Body", encrypted));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tenantId, messageId, "To", encrypted));
    }

    [Fact]
    public void Protect_UsesRandomNonce()
    {
        var protector = new AesGcmSmsContentProtector(Configuration);
        var tenantId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        Assert.NotEqual(protector.Protect(tenantId, messageId, "Body", "same"),
            protector.Protect(tenantId, messageId, "Body", "same"));
    }
}

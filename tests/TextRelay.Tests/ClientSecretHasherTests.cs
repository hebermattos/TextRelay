using Sms.Application.Auth;

namespace Sms.Infrastructure.Tests;

public sealed class ClientSecretHasherTests
{
    [Fact]
    public void HashAndVerify_RoundTripsSecret()
    {
        var value = ClientSecretHasher.Hash("strong-client-secret");
        Assert.Equal(ClientSecretHasher.HashSize, value.Hash.Length);
        Assert.Equal(ClientSecretHasher.SaltSize, value.Salt.Length);
        Assert.Equal(ClientSecretHasher.DefaultIterations, value.Iterations);
        Assert.True(ClientSecretHasher.Verify("strong-client-secret", value.Hash, value.Salt, value.Iterations));
        Assert.False(ClientSecretHasher.Verify("wrong-secret", value.Hash, value.Salt, value.Iterations));
    }

    [Fact]
    public void Hash_UsesRandomSalt()
    {
        var first = ClientSecretHasher.Hash("same-secret");
        var second = ClientSecretHasher.Hash("same-secret");
        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Hash_RejectsBlankSecret(string secret) => Assert.Throws<ArgumentException>(() => ClientSecretHasher.Hash(secret));

    [Fact]
    public void Verify_RejectsInvalidInputs()
    {
        Assert.False(ClientSecretHasher.Verify("", [1], [1], 600000));
        Assert.False(ClientSecretHasher.Verify("x", [], [1], 600000));
        Assert.False(ClientSecretHasher.Verify("x", [1], [], 600000));
        Assert.False(ClientSecretHasher.Verify("x", [1], [1], 599999));
    }
}

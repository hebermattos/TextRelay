using System.Security.Cryptography;

namespace Sms.Application.Auth;

public static class ClientSecretHasher
{
    public const int DefaultIterations = 600_000;
    public const int SaltSize = 32;
    public const int HashSize = 32;

    public static (byte[] Hash, byte[] Salt, int Iterations) Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, DefaultIterations, HashAlgorithmName.SHA256, HashSize);
        return (hash, salt, DefaultIterations);
    }

    public static bool Verify(string secret, byte[] expectedHash, byte[] salt, int iterations)
    {
        if (string.IsNullOrEmpty(secret) || expectedHash.Length == 0 || salt.Length == 0 || iterations < DefaultIterations) return false;
        var actual = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expectedHash);
    }
}

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Sms.Application.Security;

namespace Sms.Infrastructure.Security;

public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const byte Version = 1;
    private readonly byte[] _key;

    public AesGcmSecretProtector(IConfiguration configuration)
    {
        var encodedKey = configuration["Encryption:MasterKey"];
        if (string.IsNullOrWhiteSpace(encodedKey))
            throw new InvalidOperationException("Encryption:MasterKey is not configured.");

        try { _key = Convert.FromBase64String(encodedKey); }
        catch (FormatException ex) { throw new InvalidOperationException("Encryption:MasterKey must be Base64 encoded.", ex); }

        if (_key.Length != 32)
            throw new InvalidOperationException("Encryption:MasterKey must decode to exactly 32 bytes for AES-256.");
    }

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var payload = new byte[1 + nonce.Length + tag.Length + ciphertext.Length];
        payload[0] = Version;
        Buffer.BlockCopy(nonce, 0, payload, 1, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, 13, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, 29, ciphertext.Length);
        return Convert.ToBase64String(payload);
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedValue);
        var payload = Convert.FromBase64String(protectedValue);
        if (payload.Length < 29 || payload[0] != Version)
            throw new CryptographicException("Invalid encrypted secret payload.");

        var nonce = payload.AsSpan(1, 12);
        var tag = payload.AsSpan(13, 16);
        var ciphertext = payload.AsSpan(29);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }
}

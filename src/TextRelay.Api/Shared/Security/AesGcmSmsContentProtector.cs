using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Sms.Application.Security;

namespace Sms.Infrastructure.Security;

public sealed class AesGcmSmsContentProtector : ISmsContentProtector
{
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int NonceOffset = 1;
    private const int TagOffset = NonceOffset + NonceSize;
    private const int CiphertextOffset = TagOffset + TagSize;

    private readonly byte[] _masterKey;

    public AesGcmSmsContentProtector(IConfiguration configuration)
    {
        var encodedKey = configuration["Encryption:MasterKey"];
        if (string.IsNullOrWhiteSpace(encodedKey))
            throw new InvalidOperationException("Encryption:MasterKey is not configured.");

        try
        {
            _masterKey = Convert.FromBase64String(encodedKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Encryption:MasterKey must be Base64 encoded.", exception);
        }

        if (_masterKey.Length != 32)
            throw new InvalidOperationException("Encryption:MasterKey must decode to exactly 32 bytes for AES-256.");
    }

    public string Protect(Guid tenantId, Guid messageId, string field, string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(plaintext);

        var key = DeriveTenantKey(tenantId);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, AssociatedData(tenantId, messageId, field));

            var payload = new byte[CiphertextOffset + ciphertext.Length];
            payload[0] = Version;
            Buffer.BlockCopy(nonce, 0, payload, NonceOffset, nonce.Length);
            Buffer.BlockCopy(tag, 0, payload, TagOffset, tag.Length);
            Buffer.BlockCopy(ciphertext, 0, payload, CiphertextOffset, ciphertext.Length);
            return Convert.ToBase64String(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    public string Unprotect(Guid tenantId, Guid messageId, string field, string protectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedValue);

        var payload = Convert.FromBase64String(protectedValue);
        if (payload.Length < CiphertextOffset || payload[0] != Version)
            throw new CryptographicException("Invalid encrypted SMS content payload.");

        var key = DeriveTenantKey(tenantId);
        var plaintext = new byte[payload.Length - CiphertextOffset];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                payload.AsSpan(NonceOffset, NonceSize),
                payload.AsSpan(CiphertextOffset),
                payload.AsSpan(TagOffset, TagSize),
                plaintext,
                AssociatedData(tenantId, messageId, field));

            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public byte[] Fingerprint(Guid tenantId, string purpose, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentNullException.ThrowIfNull(value);

        var key = DeriveTenantKey(tenantId);
        try
        {
            return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{purpose}:{value}"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[] DeriveTenantKey(Guid tenantId) =>
        HMACSHA256.HashData(_masterKey, Encoding.UTF8.GetBytes($"sms-content-v1:{tenantId:N}"));

    private static byte[] AssociatedData(Guid tenantId, Guid messageId, string field) =>
        Encoding.UTF8.GetBytes($"{tenantId:N}:{messageId:N}:{field}");
}

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

// Vercel instances share a deployment secret and have no durable local key ring.
internal sealed class VercelDataProtector : IDataProtector
{
    private const byte FormatVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + NonceSize + TagSize;
    private readonly byte[] key;

    private VercelDataProtector(byte[] key) => this.key = key;

    public static VercelDataProtector FromConfiguration(IConfiguration config)
    {
        Span<byte> secret = stackalloc byte[32];
        try
        {
            if (!Convert.TryFromBase64String(config["ExtraMed:VercelEncryptionKey"] ?? "", secret, out var size) || size != secret.Length)
                throw new InvalidOperationException("Set ExtraMed__VercelEncryptionKey to 32 random bytes encoded as Base64.");

            return new VercelDataProtector(HMACSHA256.HashData(secret, "ExtraMed.Vercel.v1"u8));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public IDataProtector CreateProtector(string purpose) =>
        new VercelDataProtector(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(purpose)));

    public byte[] Protect(byte[] plaintext)
    {
        var payload = new byte[HeaderSize + plaintext.Length];
        payload[0] = FormatVersion;
        var nonce = payload.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, payload.AsSpan(HeaderSize), payload.AsSpan(1 + NonceSize, TagSize), payload.AsSpan(0, 1));
        return payload;
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        if (protectedData.Length < HeaderSize || protectedData[0] != FormatVersion)
            throw new CryptographicException("Unsupported ExtraMed data format.");

        var plaintext = new byte[protectedData.Length - HeaderSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(protectedData.AsSpan(1, NonceSize), protectedData.AsSpan(HeaderSize),
            protectedData.AsSpan(1 + NonceSize, TagSize), plaintext, protectedData.AsSpan(0, 1));
        return plaintext;
    }
}

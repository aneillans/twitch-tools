using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Options;

namespace TwitchTools.Web.Services.Security;

public sealed class AesDataEncryptionService(IOptions<EncryptionOptions> options) : IDataEncryptionService
{
    private const string Prefix = "enc::";
    private readonly byte[] _key = DeriveKey(options.Value.Salt);

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            return plaintext;
        }

        if (plaintext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return plaintext;
        }

        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var packed = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, packed, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, packed, nonce.Length + tag.Length, cipherBytes.Length);

        return Prefix + Convert.ToBase64String(packed);
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext) || !ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return ciphertext;
        }

        var payload = Convert.FromBase64String(ciphertext[Prefix.Length..]);
        if (payload.Length < 28)
        {
            throw new InvalidOperationException("Encrypted payload is invalid.");
        }

        var nonce = payload.AsSpan(0, 12).ToArray();
        var tag = payload.AsSpan(12, 16).ToArray();
        var cipherBytes = payload.AsSpan(28).ToArray();
        var plainBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] DeriveKey(string salt)
    {
        if (string.IsNullOrWhiteSpace(salt))
        {
            throw new InvalidOperationException("Encryption:Salt is required and must be provided by environment configuration.");
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(salt));
    }
}

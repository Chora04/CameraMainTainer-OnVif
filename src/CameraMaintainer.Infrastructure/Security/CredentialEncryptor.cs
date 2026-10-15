using System.Security.Cryptography;
using System.Text;

namespace CameraMaintainer.Infrastructure.Security;

public interface ICredentialEncryptor
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}

public sealed class AesCredentialEncryptor : ICredentialEncryptor
{
    private const string GcmPrefix = "gcm1:";
    private const int GcmNonceSize = 12;
    private const int GcmTagSize = 16;
    private const int LegacyIvSize = 16;
    private readonly byte[] _key;

    public AesCredentialEncryptor(string base64Key)
    {
        if (string.IsNullOrEmpty(base64Key))
        {
            _key = Convert.FromBase64String(GenerateKey());
        }
        else
        {
            _key = Convert.FromBase64String(base64Key);
        }

        if (_key.Length != 32)
            throw new ArgumentException("The credential encryption key must contain 32 bytes.", nameof(base64Key));
    }

    private static string GenerateKey()
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        return Convert.ToBase64String(aes.Key);
    }

    public string Encrypt(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        var nonce = RandomNumberGenerator.GetBytes(GcmNonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[GcmTagSize];

        using (var aes = new AesGcm(_key, GcmTagSize))
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var result = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, nonce.Length + tag.Length, cipherBytes.Length);
        CryptographicOperations.ZeroMemory(plainBytes);

        return GcmPrefix + Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cipherText);

        return cipherText.StartsWith(GcmPrefix, StringComparison.Ordinal)
            ? DecryptGcm(cipherText[GcmPrefix.Length..])
            : DecryptLegacyCbc(cipherText);
    }

    private string DecryptGcm(string encodedPayload)
    {
        var payload = Convert.FromBase64String(encodedPayload);
        if (payload.Length < GcmNonceSize + GcmTagSize)
            throw new CryptographicException("The encrypted credential payload is incomplete.");

        var nonce = payload.AsSpan(0, GcmNonceSize);
        var tag = payload.AsSpan(GcmNonceSize, GcmTagSize);
        var cipher = payload.AsSpan(GcmNonceSize + GcmTagSize);
        var plainBytes = new byte[cipher.Length];

        using (var aes = new AesGcm(_key, GcmTagSize))
            aes.Decrypt(nonce, cipher, tag, plainBytes);

        var result = Encoding.UTF8.GetString(plainBytes);
        CryptographicOperations.ZeroMemory(plainBytes);
        return result;
    }

    private string DecryptLegacyCbc(string cipherText)
    {
        var fullCipher = Convert.FromBase64String(cipherText);
        if (fullCipher.Length <= LegacyIvSize)
            throw new CryptographicException("The legacy credential payload is incomplete.");

        var iv = new byte[LegacyIvSize];
        var cipher = new byte[fullCipher.Length - LegacyIvSize];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, LegacyIvSize);
        Buffer.BlockCopy(fullCipher, LegacyIvSize, cipher, 0, cipher.Length);

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        var result = Encoding.UTF8.GetString(plainBytes);
        CryptographicOperations.ZeroMemory(plainBytes);
        return result;
    }
}

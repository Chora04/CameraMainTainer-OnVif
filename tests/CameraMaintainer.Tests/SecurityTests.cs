using System.Security.Cryptography;
using CameraMaintainer.Infrastructure.Security;

namespace CameraMaintainer.Tests;

public sealed class SecurityTests
{
    [Fact]
    public void PasswordHasher_VerifiesOnlyCorrectPassword()
    {
        var password = PasswordHasher.Hash("admin12345");

        Assert.True(PasswordHasher.Verify("admin12345", password.Hash, password.Salt));
        Assert.False(PasswordHasher.Verify("wrong-password", password.Hash, password.Salt));
    }

    [Theory]
    [InlineData("not-base64", "not-base64")]
    [InlineData("", "not-base64")]
    public void PasswordHasher_MalformedStorageValues_ReturnFalse(string hash, string salt)
    {
        Assert.False(PasswordHasher.Verify("password", hash, salt));
    }

    [Fact]
    public void CredentialEncryptor_RoundTripsUnicodeCredentials()
    {
        var encryptor = new AesCredentialEncryptor(string.Empty);
        const string value = "camera-admin:пароль-123";

        var encrypted = encryptor.Encrypt(value);

        Assert.NotEqual(value, encrypted);
        Assert.StartsWith("gcm1:", encrypted);
        Assert.Equal(value, encryptor.Decrypt(encrypted));
    }

    [Fact]
    public void CredentialEncryptor_UsesDifferentIvForEachEncryption()
    {
        var encryptor = new AesCredentialEncryptor(string.Empty);

        var first = encryptor.Encrypt("same-value");
        var second = encryptor.Encrypt("same-value");

        Assert.NotEqual(first, second);
        Assert.Equal("same-value", encryptor.Decrypt(first));
        Assert.Equal("same-value", encryptor.Decrypt(second));
    }

    [Fact]
    public void CredentialEncryptor_RejectsTamperedCiphertext()
    {
        var encryptor = new AesCredentialEncryptor(string.Empty);
        var encrypted = encryptor.Encrypt("camera-password");
        var payload = Convert.FromBase64String(encrypted["gcm1:".Length..]);
        payload[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() =>
            encryptor.Decrypt("gcm1:" + Convert.ToBase64String(payload)));
    }

    [Fact]
    public void CredentialEncryptor_DecryptsLegacyCbcPayload()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        const string value = "legacy-password";
        string legacy;
        using (var aes = Aes.Create())
        {
            aes.Key = key;
            aes.GenerateIV();
            using var encryptor = aes.CreateEncryptor();
            var plain = System.Text.Encoding.UTF8.GetBytes(value);
            var cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
            legacy = Convert.ToBase64String(aes.IV.Concat(cipher).ToArray());
        }

        var credentialEncryptor = new AesCredentialEncryptor(Convert.ToBase64String(key));

        Assert.Equal(value, credentialEncryptor.Decrypt(legacy));
    }
}

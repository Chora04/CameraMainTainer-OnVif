using System.Security.Cryptography;

namespace CameraMaintainer.Infrastructure.Security;

public static class PasswordHasher
{
    private const int Iterations = 120_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string password, string encodedHash, string encodedSalt)
    {
        try
        {
            var expected = Convert.FromBase64String(encodedHash);
            var salt = Convert.FromBase64String(encodedSalt);
            if (expected.Length != HashSize || salt.Length != SaltSize)
                return false;

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or CryptographicException)
        {
            return false;
        }
    }
}

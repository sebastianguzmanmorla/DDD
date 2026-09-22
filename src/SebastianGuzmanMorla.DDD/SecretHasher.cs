using System.Globalization;
using System.Security.Cryptography;

namespace SebastianGuzmanMorla.DDD;

public static class SecretHasher
{
    private const int SaltSize = 16; // 128 bits
    private const int KeySize = 32; // 256 bits
    private const int Iterations = 100_000;
    // Bound verification work even if a stored hash is corrupted or untrusted.
    private const int MaxVerificationIterations = 1_000_000;

    public static string Hash(string secret)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            secret,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize
        );

        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string secret, string storedHash)
    {
        if (secret is null || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        string[] parts = storedHash.Split('.', 3);
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int iterations) ||
            iterations <= 0 || iterations > MaxVerificationIterations)
        {
            return false;
        }

        Span<byte> salt = stackalloc byte[SaltSize];
        Span<byte> expectedKey = stackalloc byte[KeySize];
        if (!Convert.TryFromBase64String(parts[1], salt, out int saltLength) || saltLength != SaltSize ||
            !Convert.TryFromBase64String(parts[2], expectedKey, out int keyLength) || keyLength != KeySize)
        {
            return false;
        }

        byte[] actualKey = Rfc2898DeriveBytes.Pbkdf2(
            secret,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            KeySize
        );

        return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
    }

    public static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static byte[] Base64UrlDecode(string input)
    {
        string padded = input
            .Replace('-', '+')
            .Replace('_', '/');

        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }
}

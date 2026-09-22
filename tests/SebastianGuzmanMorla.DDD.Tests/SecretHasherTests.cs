using System.Security.Cryptography;

namespace SebastianGuzmanMorla.DDD.Tests;

public class SecretHasherTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("password 🔐")]
    [InlineData("")]
    public void Hash_RoundTripsAndRejectsDifferentSecret(string secret)
    {
        string hash = SecretHasher.Hash(secret);

        Assert.True(SecretHasher.Verify(secret, hash));
        Assert.False(SecretHasher.Verify(secret + "x", hash));
        Assert.NotEqual(hash, SecretHasher.Hash(secret));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("abc.AA==.AA==")]
    [InlineData("2147483648.AA==.AA==")]
    [InlineData("0.AA==.AA==")]
    [InlineData("-1.AA==.AA==")]
    [InlineData("100000.%%%.AA==")]
    [InlineData("100000.AA==.%%%")]
    [InlineData("100000.AA==.")]
    [InlineData("100000..")]
    [InlineData("100000.AA==.AA==.extra")]
    public void Verify_MalformedHashIsRejectedWithoutThrowing(string hash)
    {
        Assert.False(SecretHasher.Verify("any password", hash));
    }

    [Theory]
    [InlineData(1000001)]
    [InlineData(int.MaxValue)]
    public void Verify_RejectsExcessiveWorkFactor(int iterations)
    {
        string hash = $"{iterations}.{Convert.ToBase64String(new byte[16])}.{Convert.ToBase64String(new byte[32])}";
        Assert.False(SecretHasher.Verify("password", hash));
    }

    [Fact]
    public void Verify_PreservesLegacyIterationCounts()
    {
        byte[] salt = new byte[16];
        byte[] key = Rfc2898DeriveBytes.Pbkdf2("password", salt, 10_000, HashAlgorithmName.SHA256, 32);
        Assert.True(SecretHasher.Verify("password", $"10000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(33)]
    public void Verify_RejectsKeysOfUnexpectedLength(int keyLength)
    {
        byte[] salt = new byte[16];
        byte[] key = Rfc2898DeriveBytes.Pbkdf2("password", salt, 100_000, HashAlgorithmName.SHA256, keyLength);
        string hash = $"100000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";

        Assert.False(SecretHasher.Verify("password", hash));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(17)]
    public void Verify_RejectsSaltsOfUnexpectedLength(int saltLength)
    {
        byte[] salt = new byte[saltLength];
        byte[] key = Rfc2898DeriveBytes.Pbkdf2("password", salt, 100_000, HashAlgorithmName.SHA256, 32);
        string hash = $"100000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";

        Assert.False(SecretHasher.Verify("password", hash));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(32)]
    public void Base64Url_RoundTripsBinaryData(int length)
    {
        byte[] bytes = Enumerable.Repeat((byte)255, length).ToArray();
        string encoded = SecretHasher.Base64UrlEncode(bytes);

        Assert.DoesNotContain("=", encoded);
        Assert.DoesNotContain("/", encoded);
        Assert.DoesNotContain("+", encoded);
        Assert.Equal(bytes, SecretHasher.Base64UrlDecode(encoded));
    }
}

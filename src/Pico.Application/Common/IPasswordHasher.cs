namespace Pico.Application.Common;

/// <summary>
/// Password hashing service. Uses PBKDF2 with HMAC-SHA256 and a random salt.
/// Not Argon2id, but properly salted and slow enough for a demo.
/// Format: pbkdf2$iterations$saltBase64$hashBase64
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public string Hash(string password)
    {
        var salt = new byte[SaltSize];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, System.Security.Cryptography.HashAlgorithmName.SHA256, HashSize);

        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(hash)) return false;

        var parts = hash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;

        if (!int.TryParse(parts[1], out var iterations)) return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);

        var actual = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, System.Security.Cryptography.HashAlgorithmName.SHA256, expected.Length);

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
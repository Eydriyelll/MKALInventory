using System.Security.Cryptography;

namespace MkalInventoryApp.Services;

public static class SecurityHelper
{
    private const int Iterations = 100_000;

    public static (string Hash, string Salt) Hash(string secret)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string secret, string hash, string salt)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt)) return false;
        try
        {
            var expected = Convert.FromBase64String(hash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(secret, Convert.FromBase64String(salt), Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    public static bool IsValidPin(string? pin) =>
        pin is { Length: 4 } && pin.All(char.IsAsciiDigit);

    public static string? ValidatePassword(string? password) =>
        string.IsNullOrWhiteSpace(password) || password.Length < 6
            ? "Password must be at least 6 characters."
            : null;

    public static bool IsValidInitials(string? initials) =>
        initials is { Length: >= 2 and <= 4 } && initials.All(char.IsLetter);
}

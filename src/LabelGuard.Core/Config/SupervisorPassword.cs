using System.Security.Cryptography;

namespace LabelGuard.Core.Config;

/// <summary>Senha única do supervisor, guardada como hash PBKDF2-SHA256 (nunca em texto puro).</summary>
public static class SupervisorPassword
{
    public const int MinLength = 4;

    private const string Scheme = "pbkdf2-sha256";
    private const int Iterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>Formato: <c>pbkdf2-sha256$iterações$sal(base64)$hash(base64)</c>.</summary>
    public static string Hash(string password)
    {
        if (password.Length < MinLength)
            throw new ArgumentException($"A senha precisa ter pelo menos {MinLength} caracteres.", nameof(password));
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string? stored)
    {
        var parts = stored?.Split('$');
        if (parts is not { Length: 4 } || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations) || iterations <= 0)
            return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

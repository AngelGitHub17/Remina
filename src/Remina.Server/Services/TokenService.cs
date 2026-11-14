using System.Security.Cryptography;

namespace Remina.Server.Services;

/// <summary>
/// Génère des secrets (codes d'inscription, jetons d'agent) et les hache pour le stockage.
/// Les jetons ne sont jamais conservés en clair côté serveur.
/// </summary>
public static class TokenService
{
    /// <summary>Jeton d'agent opaque, sûr pour une URL/en-tête.</summary>
    public static string NewAgentToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>Code d'inscription court et lisible (ex : 4K7Q-9XP2-...).</summary>
    public static string NewEnrollmentCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sans I/O/0/1
        Span<byte> raw = stackalloc byte[12];
        RandomNumberGenerator.Fill(raw);
        var chars = new char[15]; // 12 symboles + 3 tirets
        int pos = 0;
        for (int i = 0; i < 12; i++)
        {
            if (i > 0 && i % 4 == 0) chars[pos++] = '-';
            chars[pos++] = alphabet[raw[i] % alphabet.Length];
        }
        return new string(chars, 0, pos);
    }

    public static string Hash(string secret)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(secret);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static bool Verify(string secret, string hash) =>
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(Hash(secret)),
            System.Text.Encoding.UTF8.GetBytes(hash));
}

using System.Security.Cryptography;

namespace Remina.Server.Services;

/// <summary>
/// Hachage des mots de passe : PBKDF2-HMAC-SHA256, sel aléatoire de 16 octets.
/// Format stocké : <c>pbkdf2-sha256$&lt;itérations&gt;$&lt;sel base64&gt;$&lt;hash base64&gt;</c> —
/// le nombre d'itérations est conservé, ce qui permet de relever le coût sans casser les
/// comptes existants (rehachage transparent à la connexion, voir <see cref="NeedsRehash"/>).
/// </summary>
public static class PasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    public const int Iterations = 600_000; // recommandation OWASP 2023 pour PBKDF2-SHA256
    private const int SaltSize = 16;
    private const int HashSize = 32;
    public const int MinLength = 12;

    // Hachage factice : sert à égaliser le temps de réponse quand l'identifiant est inconnu.
    private static readonly Lazy<string> Dummy = new(() => Hash(Guid.NewGuid().ToString()));

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            Verify(password, Dummy.Value); // même coût que pour un vrai compte
            return false;
        }
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iter) || iter < 1)
            return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    /// <summary>Vrai si le hachage a été produit avec un coût inférieur au coût courant.</summary>
    public static bool NeedsRehash(string stored)
    {
        var parts = stored.Split('$');
        return parts.Length != 4 || !int.TryParse(parts[1], out var iter) || iter < Iterations;
    }

    /// <summary>Règle de robustesse minimale ; renvoie un message d'erreur ou null.</summary>
    public static string? Validate(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < MinLength
            ? $"Le mot de passe doit faire au moins {MinLength} caractères."
            : password.Length > 256 ? "Mot de passe trop long." : null;
}

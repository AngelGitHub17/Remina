namespace Remina.Shared;

/// <summary>
/// Comparaison minimale de versions sémantiques (X.Y.Z), tolérante au préfixe « v »
/// et au suffixe (« -20261114-Remina »). Seule la partie X.Y.Z est comparée.
/// </summary>
public static class SemVer
{
    public static (int major, int minor, int patch) Parse(string version)
    {
        var s = version.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        // Coupe au premier caractère non numérique/non point (suffixe build/pré-version).
        int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0) s = s[..cut];

        var parts = s.Split('.');
        int Get(int i) => i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;
        return (Get(0), Get(1), Get(2));
    }

    /// <summary>&gt; 0 si a &gt; b, 0 si égal, &lt; 0 si a &lt; b.</summary>
    public static int Compare(string a, string b)
    {
        var (a1, a2, a3) = Parse(a);
        var (b1, b2, b3) = Parse(b);
        if (a1 != b1) return a1.CompareTo(b1);
        if (a2 != b2) return a2.CompareTo(b2);
        return a3.CompareTo(b3);
    }

    /// <summary>Vrai si <paramref name="candidate"/> est strictement plus récent que <paramref name="current"/>.</summary>
    public static bool IsNewer(string candidate, string current) => Compare(candidate, current) > 0;
}

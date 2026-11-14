namespace Remina.Shared;

/// <summary>
/// Versionnage Remina : chaque numéro majeur porte un nom de code.
/// Format complet : {Semver}-{yyyyMMdd}-{CodeName}
/// Toutes les versions 0.x.x partagent le nom de la série 0 ("Remina").
/// </summary>
public static class ReminaVersion
{
    public const string Semver = "0.0.8";

    /// <summary>Date de build, renseignée au moment de la publication.</summary>
    public const string BuildDate = "20261114";

    /// <summary>Nom de code de la série majeure courante.</summary>
    public const string CodeName = "Remina";

    public static string Full => $"{Semver}-{BuildDate}-{CodeName}";

    /// <summary>
    /// Noms de code réservés par série majeure. Le nom ne change qu'au passage
    /// à X.0.0. Les séries suivantes sont libres ; à compléter au fil des versions.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, string> MajorCodeNames =
        new Dictionary<int, string>
        {
            [0] = "Remina",
        };
}

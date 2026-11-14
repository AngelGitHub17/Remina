namespace Remina.Shared;

/// <summary>Quelle application veut se mettre à jour.</summary>
public enum UpdateApp
{
    Server = 0,
    Agent = 1,
}

/// <summary>Réponse du serveur à une demande de vérification de mise à jour.</summary>
public record UpdateCheckResult
{
    public bool UpdateAvailable { get; init; }
    public string LatestVersion { get; init; } = "";
    /// <summary>URL de l'archive ZIP à télécharger pour le couple (app, RID), si disponible.</summary>
    public string? DownloadUrl { get; init; }
}

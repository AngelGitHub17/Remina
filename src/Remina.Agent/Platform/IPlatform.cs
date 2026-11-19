using Remina.Shared;

namespace Remina.Agent.Platform;

/// <summary>
/// Abstraction des opérations propres à chaque OS. Une implémentation par plateforme ;
/// le cœur de l'agent ne connaît que cette interface.
/// </summary>
public interface IPlatform
{
    OsPlatform Os { get; }

    /// <summary>Identifiant matériel envoyé à l'inscription (puis mémorisé dans l'état local).</summary>
    string GetHardwareId();

    /// <summary>Empreinte du poste, pour vérifier au démarrage que l'identité locale est la sienne.</summary>
    HardwareFingerprint GetFingerprint();

    /// <summary>Vrai si un identifiant d'agent 0.1.12–0.1.14 correspond à ce poste.</summary>
    bool MatchesLegacyHardwareId(string hardwareId);

    /// <summary>Inventaire de base du poste.</summary>
    MachineInventory CollectInventory(string agentVersion);

    /// <summary>Nom de l'utilisateur de session courant, si connu.</summary>
    string? GetCurrentUser();

    /// <summary>
    /// Vrai si une session distante (RDP sur Windows) est active. Tant qu'elle l'est,
    /// l'agent ne relance pas la connexion automatique de session.
    /// </summary>
    bool IsRemoteSessionActive();

    /// <summary>Vrai si l'utilisateur est inactif (aucune saisie) depuis le seuil donné.</summary>
    bool IsUserIdle(TimeSpan threshold);

    /// <summary>Verrouille la session de l'utilisateur.</summary>
    void Lock();

    /// <summary>Redémarre le poste.</summary>
    void Reboot();

    /// <summary>Affiche un message à l'utilisateur (best-effort selon l'OS/la session).</summary>
    void ShowMessage(string text);

    /// <summary>
    /// Appli au premier plan : nom du processus et titre de fenêtre, ou null si indéterminé.
    /// Utilisé par le suivi d'activité.
    /// </summary>
    (string App, string? Title)? GetForegroundApp();

    /// <summary>Liste des imprimantes installées (pour le choix dans l'interface).</summary>
    IReadOnlyList<string> GetPrinters();

    /// <summary>
    /// Configure la connexion de session automatique (Windows). Retourne vrai si appliqué.
    /// Nécessite des droits administrateur (service/LocalSystem).
    /// </summary>
    bool ConfigureAutoLogin(string user, string password, string? domain);

    /// <summary>Désactive la connexion de session automatique.</summary>
    bool DisableAutoLogin();

    /// <summary>Liste des logiciels installés (nom + version) pour l'inventaire.</summary>
    IReadOnlyList<InventorySoftware> GetInstalledSoftware();
}

/// <summary>
/// Empreinte d'un poste : base OS hachée, MAC physiques et nom d'hôte. Deux empreintes
/// désignent le même poste si la base OS est identique et qu'au moins une MAC physique est
/// commune (ou, faute de MAC exploitable, si le nom d'hôte est identique). Un clone (même
/// MachineGuid, autres cartes réseau) est donc bien distingué, sans que l'ordre ou l'état des
/// cartes au démarrage ne fasse perdre l'inscription.
/// </summary>
public sealed record HardwareFingerprint(string OsBaseHash, IReadOnlyList<string> Macs, string Hostname)
{
    public bool SameMachineAs(string osBaseHash, IReadOnlyCollection<string> macs, string hostname)
    {
        if (!string.Equals(OsBaseHash, osBaseHash, StringComparison.OrdinalIgnoreCase)) return false;
        if (Macs.Count > 0 && macs.Count > 0)
            return Macs.Intersect(macs, StringComparer.OrdinalIgnoreCase).Any();
        return string.Equals(Hostname, hostname, StringComparison.OrdinalIgnoreCase);
    }
}

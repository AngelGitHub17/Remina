using Remina.Shared;

namespace Remina.Agent.Platform;

/// <summary>
/// Abstraction des opérations propres à chaque OS. Une implémentation par plateforme ;
/// le cœur de l'agent ne connaît que cette interface.
/// </summary>
public interface IPlatform
{
    OsPlatform Os { get; }

    /// <summary>Identifiant matériel stable (survit aux redémarrages et réinstallations agent).</summary>
    string GetHardwareId();

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

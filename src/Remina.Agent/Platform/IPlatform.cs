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
}

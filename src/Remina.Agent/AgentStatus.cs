using Remina.Shared;

namespace Remina.Agent;

/// <summary>
/// État courant de l'agent, partagé avec une éventuelle interface graphique.
/// Mis à jour par AgentWorker ; lu par l'UI (barre des tâches / fenêtre d'état).
/// </summary>
public class AgentStatus
{
    public string Version { get; } = ReminaVersion.Full;
    public string ServerUrl { get; set; } = "";
    public bool Enrolled { get; set; }
    public string MachineName { get; set; } = "";
    public DateTimeOffset? LastHeartbeatOk { get; set; }
    public bool RemoteSessionActive { get; set; }
    public string? LastError { get; set; }

    /// <summary>Suivi d'activité activé (config).</summary>
    public bool ActivityEnabled { get; set; }
    /// <summary>Mode privé : met en pause le suivi d'activité (basculable depuis l'UI).</summary>
    public bool PrivateMode { get; set; }

    public bool Online =>
        LastHeartbeatOk is not null &&
        DateTimeOffset.UtcNow - LastHeartbeatOk < TimeSpan.FromMinutes(1);

    /// <summary>Déclenché à chaque changement d'état (pour rafraîchir l'UI).</summary>
    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>Demande d'impression du ticket (texte). L'UI, si présente, affiche le dialogue.</summary>
    public event Action<string>? PrintTicketRequested;
    public bool HasPrintHandler => PrintTicketRequested != null;
    public void RequestPrint(string text) => PrintTicketRequested?.Invoke(text);
}

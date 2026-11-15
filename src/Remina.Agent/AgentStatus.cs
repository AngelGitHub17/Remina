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

    public bool Online =>
        LastHeartbeatOk is not null &&
        DateTimeOffset.UtcNow - LastHeartbeatOk < TimeSpan.FromMinutes(1);

    /// <summary>Déclenché à chaque changement d'état (pour rafraîchir l'UI).</summary>
    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();
}

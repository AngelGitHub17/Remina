using System.Text.Json;
using Remina.Shared;

namespace Remina.Agent;

/// <summary>
/// Identité locale du poste, persistée après inscription.
/// Contient le jeton d'agent : le fichier doit rester lisible par le seul service.
/// </summary>
public class AgentState
{
    public Guid MachineId { get; set; }
    public string AgentToken { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string HardwareId { get; set; } = "";
    /// <summary>URL du serveur retenue à l'inscription (survit à une config qui pointe ailleurs).</summary>
    public string ServerUrl { get; set; } = "";
    /// <summary>Imprimante choisie pour les tickets (mémorisée après le premier choix).</summary>
    public string PrinterName { get; set; } = "";

    private static string StatePath =>
        Path.Combine(ReminaPaths.DataDir, "remina-agent.json");

    public static AgentState? Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return null;
            return JsonSerializer.Deserialize<AgentState>(File.ReadAllText(StatePath));
        }
        catch { return null; }
    }

    public void Save() =>
        File.WriteAllText(StatePath,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

    public bool IsEnrolled => MachineId != Guid.Empty && !string.IsNullOrEmpty(AgentToken);
}

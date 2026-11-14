namespace Remina.Shared;

/// <summary>
/// Contrats d'échange entre l'agent et le serveur Remina.
/// Volontairement en DTO simples pour rester sérialisables JSON sur tous les OS.
/// </summary>

/// <summary>Système d'exploitation du poste, remonté par l'agent.</summary>
public enum OsPlatform
{
    Unknown = 0,
    Windows = 1,
    MacOs = 2,
    Linux = 3,
}

/// <summary>Inventaire matériel/logiciel minimal remonté à l'inscription.</summary>
public record MachineInventory
{
    public OsPlatform Os { get; init; }
    public string OsVersion { get; init; } = "";
    public string Hostname { get; init; } = "";
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? CpuName { get; init; }
    public int? CpuCores { get; init; }
    public long? TotalRamBytes { get; init; }
    public string? PrimaryMacAddress { get; init; }
    public string AgentVersion { get; init; } = "";
}

/// <summary>
/// Requête d'inscription d'un poste (façon GLPI).
/// L'admin crée d'abord un code d'inscription dans la console ; l'agent le présente ici.
/// </summary>
public record EnrollRequest
{
    public string EnrollmentCode { get; init; } = "";
    /// <summary>Identifiant matériel stable généré par l'agent (survit aux redémarrages).</summary>
    public string HardwareId { get; init; } = "";
    public MachineInventory Inventory { get; init; } = new();
}

/// <summary>Réponse d'inscription : identité du poste et jeton d'agent à conserver.</summary>
public record EnrollResponse
{
    public Guid MachineId { get; init; }
    public string AgentToken { get; init; } = "";
    public string MachineName { get; init; } = "";
}

/// <summary>Battement de cœur périodique : garde le poste « en ligne » et remonte l'état.</summary>
public record HeartbeatRequest
{
    public Guid MachineId { get; init; }
    /// <summary>Utilisateur de session courant, si connu.</summary>
    public string? CurrentUser { get; init; }
    /// <summary>Vrai si une session distante (RDP) est active : l'auto-login reste en pause.</summary>
    public bool RemoteSessionActive { get; init; }
    /// <summary>Vrai si le poste considère l'utilisateur inactif (aucune saisie).</summary>
    public bool UserIdle { get; init; }
    public string AgentVersion { get; init; } = "";
}

/// <summary>Ordres que le serveur peut renvoyer à l'agent en réponse d'un heartbeat.</summary>
public record HeartbeatResponse
{
    public IReadOnlyList<AgentCommand> Commands { get; init; } = Array.Empty<AgentCommand>();
    public int NextHeartbeatSeconds { get; init; } = 30;
}

public enum AgentCommandType
{
    None = 0,
    Lock = 1,
    Reboot = 2,
    ShowMessage = 3,
    PrintSummaryTicket = 4,
    AutoLogin = 5,
}

public record AgentCommand
{
    public Guid CommandId { get; init; } = Guid.NewGuid();
    public AgentCommandType Type { get; init; }
    public string? Payload { get; init; }
}

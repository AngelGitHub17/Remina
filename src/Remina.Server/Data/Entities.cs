using Remina.Shared;

namespace Remina.Server.Data;

/// <summary>Poste inscrit dans le parc Remina.</summary>
public class Machine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string HardwareId { get; set; } = "";
    public string Name { get; set; } = "";
    public OsPlatform Os { get; set; }
    public string OsVersion { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? CpuName { get; set; }
    public int? CpuCores { get; set; }
    public long? TotalRamBytes { get; set; }
    public string? PrimaryMacAddress { get; set; }
    public string AgentVersion { get; set; } = "";

    /// <summary>Hachage du jeton d'agent (jamais stocké en clair).</summary>
    public string AgentTokenHash { get; set; } = "";

    public DateTimeOffset EnrolledAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; set; }
    public string? CurrentUser { get; set; }
    public bool RemoteSessionActive { get; set; }
    public bool UserIdle { get; set; }

    public List<AgentCommandRecord> Commands { get; set; } = new();
}

/// <summary>
/// Code d'inscription à usage limité, généré par l'admin (façon GLPI).
/// </summary>
public class EnrollmentCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string? Label { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    /// <summary>Nombre d'inscriptions encore autorisées avec ce code.</summary>
    public int RemainingUses { get; set; } = 1;
    public bool IsRevoked { get; set; }

    public bool IsUsable(DateTimeOffset now) =>
        !IsRevoked
        && RemainingUses > 0
        && (ExpiresAt is null || ExpiresAt > now);
}

/// <summary>Commande en attente de récupération par un agent.</summary>
public class AgentCommandRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MachineId { get; set; }
    public Machine? Machine { get; set; }
    public AgentCommandType Type { get; set; }
    public string? Payload { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeliveredAt { get; set; }
}

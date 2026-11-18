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

    /// <summary>Bureau (local) auquel le poste est rattaché, si connu.</summary>
    public Guid? BureauId { get; set; }
    public Bureau? Bureau { get; set; }

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
/// Bureau = un local, un groupe de postes (façon « entité »/« lieu » de GLPI).
/// Un poste appartient à au plus un bureau.
/// </summary>
public class Bureau
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Emplacement physique (bâtiment, salle, adresse courte...).</summary>
    public string? Location { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Machine> Machines { get; set; } = new();
}

/// <summary>
/// Code d'inscription à usage limité, généré par l'admin (façon GLPI).
/// </summary>
public class EnrollmentCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string? Label { get; set; }
    /// <summary>Si défini, tout poste inscrit avec ce code rejoint ce bureau.</summary>
    public Guid? BureauId { get; set; }
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

/// <summary>Agrégat d'activité : temps passé par un poste sur une appli un jour donné.</summary>
public class ActivityAggregate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MachineId { get; set; }
    public Machine? Machine { get; set; }
    public DateOnly Day { get; set; }
    public string App { get; set; } = "";
    public string Title { get; set; } = ""; // "" quand la capture des titres est désactivée
    public int Seconds { get; set; }
}

/// <summary>
/// Entrée de temps saisie à la main (ex : depuis le téléphone) pour le récapitulatif.
/// « J'ai travaillé sur Rémina de 14:00 à 16:30 » → projet + début + fin.
/// </summary>
public class ManualTimeEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Project { get; set; } = "";
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public string? Note { get; set; }
    public string Source { get; set; } = "manuel";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int Minutes => (int)Math.Max(0, (End - Start).TotalMinutes);
}

/// <summary>Installeur de logiciel téléversé, déployable sur les postes.</summary>
public class Package
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public OsPlatform Os { get; set; }
    public string? SilentArgs { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
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

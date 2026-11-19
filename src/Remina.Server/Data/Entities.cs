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

    // Résultat remonté par l'agent (ex : installation).
    public bool? Success { get; set; }
    public int? ExitCode { get; set; }
    public string? ResultMessage { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>Un logiciel installé sur un poste (inventaire).</summary>
public class InventoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MachineId { get; set; }
    public string Name { get; set; } = "";
    public string? Version { get; set; }
}

/// <summary>Rôle d'un compte de la console.</summary>
public enum AdminRole
{
    /// <summary>Consultation (et saisie de temps) ; aucune action sur le parc.</summary>
    ReadOnly = 0,
    /// <summary>Tous les droits, y compris la gestion des comptes.</summary>
    Admin = 1,
}

/// <summary>Compte de la console d'administration.</summary>
public class AdminUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identifiant de connexion (comparé en minuscules).</summary>
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>Hachage PBKDF2 (voir PasswordHasher). Null pour un compte uniquement SSO.</summary>
    public string? PasswordHash { get; set; }
    public AdminRole Role { get; set; } = AdminRole.ReadOnly;
    public bool IsDisabled { get; set; }
    public int FailedLogins { get; set; }
    public DateTimeOffset? LockoutUntil { get; set; }
    /// <summary>Fournisseur d'identité externe (ex : « nounours »), réservé au SSO.</summary>
    public string? ExternalProvider { get; set; }
    public string? ExternalId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
}

/// <summary>Session de console (cookie). Seul le hachage du jeton est stocké.</summary>
public class AdminSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TokenHash { get; set; } = "";
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Expiration absolue.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>Réglage serveur modifiable depuis la console (clé/valeur).</summary>
public class ServerSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>
/// Entrée du journal d'audit (qui / quoi / quand). <see cref="AtUnixMs"/> double la date en
/// entier : SQLite ne sait ni trier ni comparer les DateTimeOffset côté SQL, et le journal est
/// trop gros pour être chargé entièrement en mémoire.
/// </summary>
public class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public long AtUnixMs { get; set; }
    public Guid? UserId { get; set; }
    public string Username { get; set; } = "";
    /// <summary>Code d'action, ex. « auth.login », « POST /api/admin/machines/{id}/commands ».</summary>
    public string Action { get; set; } = "";
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    /// <summary>Détail court (corps JSON expurgé des secrets, tronqué).</summary>
    public string? Details { get; set; }
    public string? Ip { get; set; }
    public bool Success { get; set; } = true;
    public int? StatusCode { get; set; }
}

/// <summary>Jeton d'API personnel (scripts), au rôle de son compte. Seul le hachage est stocké.</summary>
public class ApiToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    /// <summary>Début du jeton (affichage, pour le reconnaître).</summary>
    public string Prefix { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

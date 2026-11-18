namespace Remina.Shared;

/// <summary>Une tranche d'activité agrégée : temps passé sur une appli un jour donné.</summary>
public record ActivityEntry
{
    public DateOnly Day { get; init; }
    /// <summary>Nom du processus au premier plan (ex : « firefox », « Code »).</summary>
    public string App { get; init; } = "";
    /// <summary>Titre de fenêtre, seulement si la capture des titres est activée.</summary>
    public string? Title { get; init; }
    /// <summary>Secondes cumulées de premier plan.</summary>
    public int Seconds { get; init; }
}

/// <summary>Rapport d'activité envoyé périodiquement par l'agent (deltas depuis le dernier envoi).</summary>
public record ActivityReport
{
    public Guid MachineId { get; init; }
    public IReadOnlyList<ActivityEntry> Entries { get; init; } = Array.Empty<ActivityEntry>();
}

/// <summary>Entrée de temps envoyée par l'agent (étape de récap lancée/arrêtée depuis l'interface).</summary>
public record AgentTimeEntryReport
{
    public Guid MachineId { get; init; }
    public string Project { get; init; } = "";
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
    public string? Note { get; init; }
}

namespace Remina.Shared;

/// <summary>Un logiciel installé sur un poste.</summary>
public record InventorySoftware
{
    public string Name { get; init; } = "";
    public string? Version { get; init; }
}

/// <summary>Inventaire logiciel complet remonté par l'agent (remplace l'existant).</summary>
public record InventoryReport
{
    public Guid MachineId { get; init; }
    public IReadOnlyList<InventorySoftware> Items { get; init; } = Array.Empty<InventorySoftware>();
}

/// <summary>Résultat d'exécution d'une commande (ex : installation) remonté par l'agent.</summary>
public record CommandResult
{
    public Guid MachineId { get; init; }
    public Guid CommandId { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string? Message { get; init; }
}

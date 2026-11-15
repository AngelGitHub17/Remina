using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Remina.Server.Data;
using Remina.Server.Hubs;
using Remina.Shared;

namespace Remina.Server.Services;

/// <summary>
/// Déclenche l'impression du ticket récapitulatif à une heure programmée (ex : fermeture
/// du bureau). Activé par Remina:Recap:AutoPrint = true, à l'heure Remina:Recap:Hour/Minute.
/// Le ticket part vers le poste nommé Remina:Recap:MachineName, ou le premier poste vu récemment.
/// </summary>
public class RecapScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IHubContext<AgentHub> _hub;
    private readonly IConfiguration _cfg;
    private readonly ILogger<RecapScheduler> _log;
    private DateOnly _lastSent = DateOnly.MinValue;

    public RecapScheduler(IServiceScopeFactory scopes, IHubContext<AgentHub> hub,
        IConfiguration cfg, ILogger<RecapScheduler> log)
    {
        _scopes = scopes; _hub = hub; _cfg = cfg; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await TickAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { _log.LogWarning(ex, "Planificateur de ticket : erreur."); }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (!_cfg.GetValue("Remina:Recap:AutoPrint", false)) return;

        var hour = _cfg.GetValue("Remina:Recap:Hour", 20);
        var minute = _cfg.GetValue("Remina:Recap:Minute", 0);
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);

        if (_lastSent == today) return;                 // déjà envoyé aujourd'hui
        if (now.Hour != hour || now.Minute < minute) return;

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReminaDbContext>();

        var target = _cfg["Remina:Recap:MachineName"];
        Machine? machine = string.IsNullOrWhiteSpace(target)
            ? await db.Machines.OrderByDescending(m => m.LastSeenAt).FirstOrDefaultAsync(ct)
            : await db.Machines.FirstOrDefaultAsync(m => m.Name == target, ct);

        if (machine is null) { _log.LogInformation("Ticket programmé : aucun poste cible."); _lastSent = today; return; }

        var text = await TicketService.BuildTextAsync(db, today);
        var cmd = new AgentCommandRecord
        {
            MachineId = machine.Id,
            Type = AgentCommandType.PrintSummaryTicket,
            Payload = text,
        };
        db.AgentCommands.Add(cmd);
        await db.SaveChangesAsync(ct);

        await _hub.Clients.Group(machine.Id.ToString()).SendAsync("command", new AgentCommand
        {
            CommandId = cmd.Id, Type = cmd.Type, Payload = cmd.Payload,
        }, ct);

        _lastSent = today;
        _log.LogInformation("Ticket récapitulatif programmé envoyé à {Name}.", machine.Name);
    }
}

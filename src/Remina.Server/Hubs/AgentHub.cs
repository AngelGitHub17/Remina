using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Remina.Server.Data;
using Remina.Server.Services;

namespace Remina.Server.Hubs;

/// <summary>
/// Canal temps réel serveur → agent. L'agent se connecte avec son identifiant de poste et
/// son jeton (en query string) ; il rejoint alors un groupe nommé d'après son MachineId.
/// Le serveur pousse les commandes à ce groupe dès qu'elles sont mises en file.
/// </summary>
public class AgentHub : Hub
{
    private readonly ReminaDbContext _db;

    public AgentHub(ReminaDbContext db) => _db = db;

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var machineIdStr = http?.Request.Query["machineId"].ToString();
        var token = http?.Request.Query["token"].ToString();

        if (Guid.TryParse(machineIdStr, out var machineId) && !string.IsNullOrEmpty(token))
        {
            var machine = await _db.Machines.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == machineId);
            if (machine is not null && TokenService.Verify(token, machine.AgentTokenHash))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, machineId.ToString());
                await base.OnConnectedAsync();
                return;
            }
        }

        // Identité invalide : on refuse la connexion.
        Context.Abort();
    }
}

using Microsoft.EntityFrameworkCore;
using Remina.Server.Data;
using Remina.Server.Services;
using Remina.Shared;

namespace Remina.Server.Endpoints;

public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/agent");

        // Inscription d'un poste : l'agent présente un code d'inscription valide.
        g.MapPost("/enroll", async (EnrollRequest req, ReminaDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.EnrollmentCode) ||
                string.IsNullOrWhiteSpace(req.HardwareId))
                return Results.BadRequest(new { error = "Code ou identifiant matériel manquant." });

            var now = DateTimeOffset.UtcNow;
            var code = await db.EnrollmentCodes
                .FirstOrDefaultAsync(c => c.Code == req.EnrollmentCode);

            if (code is null || !code.IsUsable(now))
                return Results.Json(new { error = "Code d'inscription invalide ou expiré." },
                    statusCode: StatusCodes.Status403Forbidden);

            // Réinscription du même matériel : on réémet un jeton au lieu de dupliquer.
            var machine = await db.Machines
                .FirstOrDefaultAsync(m => m.HardwareId == req.HardwareId);

            var token = TokenService.NewAgentToken();

            if (machine is null)
            {
                machine = new Machine
                {
                    HardwareId = req.HardwareId,
                    Name = string.IsNullOrWhiteSpace(req.Inventory.Hostname)
                        ? "poste-" + req.HardwareId[..Math.Min(6, req.HardwareId.Length)]
                        : req.Inventory.Hostname,
                };
                db.Machines.Add(machine);
            }

            ApplyInventory(machine, req.Inventory);
            machine.AgentTokenHash = TokenService.Hash(token);
            machine.LastSeenAt = now;

            code.RemainingUses--;

            await db.SaveChangesAsync();

            return Results.Ok(new EnrollResponse
            {
                MachineId = machine.Id,
                AgentToken = token,
                MachineName = machine.Name,
            });
        });

        // Battement de cœur : authentifié par le jeton d'agent (en-tête X-Agent-Token).
        g.MapPost("/heartbeat", async (HeartbeatRequest req, HttpContext http, ReminaDbContext db) =>
        {
            var token = http.Request.Headers["X-Agent-Token"].ToString();
            if (string.IsNullOrEmpty(token))
                return Results.Unauthorized();

            var machine = await db.Machines
                .Include(m => m.Commands)
                .FirstOrDefaultAsync(m => m.Id == req.MachineId);

            if (machine is null || !TokenService.Verify(token, machine.AgentTokenHash))
                return Results.Unauthorized();

            var now = DateTimeOffset.UtcNow;
            machine.LastSeenAt = now;
            machine.CurrentUser = req.CurrentUser;
            machine.RemoteSessionActive = req.RemoteSessionActive;
            machine.UserIdle = req.UserIdle;
            if (!string.IsNullOrWhiteSpace(req.AgentVersion))
                machine.AgentVersion = req.AgentVersion;

            // Récupère les commandes non encore livrées et les marque livrées.
            var pending = machine.Commands
                .Where(c => c.DeliveredAt is null)
                .OrderBy(c => c.CreatedAt)
                .ToList();

            foreach (var c in pending) c.DeliveredAt = now;

            await db.SaveChangesAsync();

            return Results.Ok(new HeartbeatResponse
            {
                NextHeartbeatSeconds = 30,
                Commands = pending.Select(c => new AgentCommand
                {
                    CommandId = c.Id,
                    Type = c.Type,
                    Payload = c.Payload,
                }).ToList(),
            });
        });
    }

    private static void ApplyInventory(Machine m, MachineInventory inv)
    {
        m.Os = inv.Os;
        m.OsVersion = inv.OsVersion;
        m.Hostname = inv.Hostname;
        m.Manufacturer = inv.Manufacturer;
        m.Model = inv.Model;
        m.CpuName = inv.CpuName;
        m.CpuCores = inv.CpuCores;
        m.TotalRamBytes = inv.TotalRamBytes;
        m.PrimaryMacAddress = inv.PrimaryMacAddress;
        m.AgentVersion = inv.AgentVersion;
    }
}

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Remina.Server.Data;
using Remina.Server.Hubs;
using Remina.Server.Services;
using Remina.Shared;

namespace Remina.Server.Endpoints;

/// <summary>
/// Endpoints d'administration (console). Protégés par une clé d'API admin
/// (en-tête X-Admin-Key) définie dans la configuration : Admin:ApiKey.
/// En v0 c'est volontairement simple ; NounoursAccount prendra le relais plus tard.
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin");

        g.MapPost("/enrollment-codes", async (
            CreateEnrollmentCodeDto dto, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();

            // Un bureau peut être attaché au code : les postes inscrits le rejoindront.
            if (dto.BureauId is not null && !await db.Bureaus.AnyAsync(b => b.Id == dto.BureauId))
                return Results.BadRequest(new { error = "Bureau inconnu." });

            var code = new EnrollmentCode
            {
                Code = TokenService.NewEnrollmentCode(),
                Label = dto.Label,
                BureauId = dto.BureauId,
                RemainingUses = dto.MaxUses <= 0 ? 1 : dto.MaxUses,
                ExpiresAt = dto.ExpiresInHours > 0
                    ? DateTimeOffset.UtcNow.AddHours(dto.ExpiresInHours)
                    : null,
            };
            db.EnrollmentCodes.Add(code);
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                code.Code, code.Label, code.BureauId, code.RemainingUses, code.ExpiresAt,
            });
        });

        // --- Bureaux (locaux) ---

        g.MapPost("/bureaus", async (
            CreateBureauDto dto, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(dto.Name))
                return Results.BadRequest(new { error = "Nom de bureau requis." });
            if (await db.Bureaus.AnyAsync(b => b.Name == dto.Name))
                return Results.Conflict(new { error = "Un bureau porte déjà ce nom." });

            var bureau = new Bureau
            {
                Name = dto.Name.Trim(),
                Description = dto.Description,
                Location = dto.Location,
            };
            db.Bureaus.Add(bureau);
            await db.SaveChangesAsync();
            return Results.Ok(new { bureau.Id, bureau.Name, bureau.Description, bureau.Location });
        });

        g.MapGet("/bureaus", async (HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();

            var bureaus = await db.Bureaus
                .OrderBy(b => b.Name)
                .Select(b => new
                {
                    b.Id, b.Name, b.Description, b.Location,
                    MachineCount = b.Machines.Count,
                })
                .ToListAsync();
            return Results.Ok(bureaus);
        });

        g.MapGet("/bureaus/{id:guid}/machines", async (
            Guid id, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();
            if (!await db.Bureaus.AnyAsync(b => b.Id == id)) return Results.NotFound();

            var machines = await db.Machines
                .Where(m => m.BureauId == id)
                .OrderBy(m => m.Name)
                .Select(m => new { m.Id, m.Name, m.Os, m.CurrentUser, m.LastSeenAt })
                .ToListAsync();
            return Results.Ok(machines);
        });

        // Activité d'un poste pour un jour donné (par défaut aujourd'hui), cumul par appli.
        g.MapGet("/machines/{id:guid}/activity", async (
            Guid id, string? day, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();

            var d = DateOnly.TryParse(day, out var parsed)
                ? parsed : DateOnly.FromDateTime(DateTime.Now);

            var apps = await db.Activity
                .Where(a => a.MachineId == id && a.Day == d)
                .GroupBy(a => a.App)
                .Select(g => new { App = g.Key, Seconds = g.Sum(x => x.Seconds) })
                .OrderByDescending(x => x.Seconds)
                .ToListAsync();

            return Results.Ok(new { day = d.ToString("yyyy-MM-dd"), apps });
        });

        // Rattache (ou détache avec bureauId=null) un poste à un bureau.
        g.MapPost("/machines/{id:guid}/bureau", async (
            Guid id, AssignBureauDto dto, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();

            var machine = await db.Machines.FindAsync(id);
            if (machine is null) return Results.NotFound();

            if (dto.BureauId is not null && !await db.Bureaus.AnyAsync(b => b.Id == dto.BureauId))
                return Results.BadRequest(new { error = "Bureau inconnu." });

            machine.BureauId = dto.BureauId;
            await db.SaveChangesAsync();
            return Results.Ok(new { machine.Id, machine.BureauId });
        });

        g.MapGet("/machines", async (HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();

            var now = DateTimeOffset.UtcNow;
            var machines = await db.Machines
                .OrderBy(m => m.Name)
                .Select(m => new
                {
                    m.Id, m.Name, m.Hostname, m.Os, m.OsVersion,
                    m.CurrentUser, m.RemoteSessionActive, m.UserIdle,
                    m.LastSeenAt, m.AgentVersion,
                    m.BureauId,
                    BureauName = m.Bureau != null ? m.Bureau.Name : null,
                    Online = m.LastSeenAt != null && m.LastSeenAt > now.AddMinutes(-2),
                })
                .ToListAsync();

            return Results.Ok(machines);
        });

        // Met une commande en file pour un poste (verrouiller, redémarrer, ticket, message...).
        g.MapPost("/machines/{id:guid}/commands", async (
            Guid id, QueueCommandDto dto, HttpContext http, IConfiguration cfg,
            ReminaDbContext db, IHubContext<AgentHub> hub) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();

            var machine = await db.Machines.FindAsync(id);
            if (machine is null) return Results.NotFound();

            var cmd = new AgentCommandRecord
            {
                MachineId = id,
                Type = dto.Type,
                Payload = dto.Payload,
            };
            db.AgentCommands.Add(cmd);
            await db.SaveChangesAsync();

            // Push temps réel : l'agent connecté reçoit l'ordre immédiatement.
            // Reste en file : le heartbeat sert de filet si l'agent n'est pas connecté au hub.
            await hub.Clients.Group(id.ToString()).SendAsync("command", new AgentCommand
            {
                CommandId = cmd.Id,
                Type = cmd.Type,
                Payload = cmd.Payload,
            });

            return Results.Ok(new { cmd.Id, cmd.Type, cmd.Payload, cmd.CreatedAt });
        });
    }

    private static bool IsAdmin(HttpContext http, IConfiguration cfg)
    {
        var expected = cfg["Admin:ApiKey"];
        if (string.IsNullOrEmpty(expected)) return false; // pas de clé = admin fermé
        var given = http.Request.Headers["X-Admin-Key"].ToString();
        return !string.IsNullOrEmpty(given) && given == expected;
    }

    public record CreateEnrollmentCodeDto(string? Label, int MaxUses, int ExpiresInHours, Guid? BureauId);
    public record QueueCommandDto(AgentCommandType Type, string? Payload);
    public record CreateBureauDto(string Name, string? Description, string? Location);
    public record AssignBureauDto(Guid? BureauId);
}

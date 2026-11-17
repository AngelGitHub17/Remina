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

        // Liste des codes d'inscription (actifs et utilisables en premier).
        g.MapGet("/enrollment-codes", async (HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();
            var now = DateTimeOffset.UtcNow;
            // SQLite ne trie pas les DateTimeOffset : on matérialise puis on trie en mémoire.
            var all = await db.EnrollmentCodes.ToListAsync();
            var codes = all.OrderByDescending(c => c.CreatedAt)
                .Select(c => new
                {
                    c.Id, c.Code, c.Label, c.BureauId, c.RemainingUses, c.ExpiresAt, c.IsRevoked,
                    Usable = c.IsUsable(now),
                })
                .ToList();
            return Results.Ok(codes);
        });

        // Révoquer un code d'inscription.
        g.MapPost("/enrollment-codes/{id:guid}/revoke", async (
            Guid id, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();
            var c = await db.EnrollmentCodes.FindAsync(id);
            if (c is null) return Results.NotFound();
            c.IsRevoked = true;
            await db.SaveChangesAsync();
            return Results.Ok();
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

        // Supprimer un bureau (ses postes sont détachés, pas supprimés).
        g.MapDelete("/bureaus/{id:guid}", async (
            Guid id, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();
            var b = await db.Bureaus.FindAsync(id);
            if (b is null) return Results.NotFound();
            db.Bureaus.Remove(b);
            await db.SaveChangesAsync();
            return Results.Ok();
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

        // Tableau de bord : indicateurs + top applis du jour.
        g.MapGet("/stats", async (HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();
            var now = DateTimeOffset.UtcNow;
            var today = DateOnly.FromDateTime(DateTime.Now);

            // Matérialisé en mémoire : SQLite ne traduit ni GroupBy(enum) ni comparaison DateTimeOffset.
            var machines = await db.Machines.ToListAsync();
            var total = machines.Count;
            var online = machines.Count(m => m.LastSeenAt != null && m.LastSeenAt > now.AddMinutes(-2));
            var bureaus = await db.Bureaus.CountAsync();
            var codes = await db.EnrollmentCodes.ToListAsync();
            var codesActive = codes.Count(c => c.IsUsable(now));

            var acts = await db.Activity.Where(a => a.Day == today).ToListAsync();
            var topApps = acts.GroupBy(a => a.App)
                .Select(gr => new { app = gr.Key, seconds = gr.Sum(x => x.Seconds) })
                .OrderByDescending(x => x.seconds).Take(6).ToList();

            var manualToday = (await db.TimeEntries.ToListAsync())
                .Where(e => DateOnly.FromDateTime(e.Start.LocalDateTime) == today)
                .Sum(e => e.Minutes);

            var byOs = machines.GroupBy(m => m.Os)
                .Select(gr => new { os = (int)gr.Key, count = gr.Count() }).ToList();

            return Results.Ok(new
            {
                totalMachines = total, onlineMachines = online, bureaus, codesActive,
                todayActivitySeconds = acts.Sum(a => a.Seconds), todayManualMinutes = manualToday,
                topApps, byOs,
            });
        });

        // Historique des commandes d'un poste.
        g.MapGet("/machines/{id:guid}/commands", async (
            Guid id, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();
            var cmds = (await db.AgentCommands.Where(c => c.MachineId == id).ToListAsync())
                .OrderByDescending(c => c.CreatedAt).Take(25)
                .Select(c => new
                {
                    c.Id, c.Type, c.CreatedAt, c.DeliveredAt,
                    Preview = c.Type == AgentCommandType.ShowMessage
                        ? (c.Payload ?? "") : (c.Payload != null ? "(contenu)" : null),
                }).ToList();
            return Results.Ok(cmds);
        });

        // --- Suppression d'un poste (cascade : activité et commandes) ---
        g.MapDelete("/machines/{id:guid}", async (
            Guid id, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();
            var machine = await db.Machines.FindAsync(id);
            if (machine is null) return Results.NotFound();
            db.Machines.Remove(machine);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // --- Entrées de temps manuelles (récap, saisie téléphone) ---
        g.MapPost("/time-entries", async (
            CreateTimeEntryDto dto, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(dto.Project)) return Results.BadRequest(new { error = "Projet requis." });
            if (dto.End <= dto.Start) return Results.BadRequest(new { error = "La fin doit être après le début." });

            var e = new ManualTimeEntry
            {
                Project = dto.Project.Trim(),
                Start = dto.Start, End = dto.End,
                Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
                Source = string.IsNullOrWhiteSpace(dto.Source) ? "manuel" : dto.Source!.Trim(),
            };
            db.TimeEntries.Add(e);
            await db.SaveChangesAsync();
            return Results.Ok(new { e.Id, e.Project, e.Start, e.End, e.Minutes, e.Note, e.Source });
        });

        g.MapGet("/time-entries", async (
            string? from, string? to, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();

            // SQLite ne trie pas les DateTimeOffset : on matérialise puis filtre/trie en mémoire.
            var all = await db.TimeEntries.ToListAsync();
            if (DateTimeOffset.TryParse(from, out var f)) all = all.Where(e => e.End >= f).ToList();
            if (DateTimeOffset.TryParse(to, out var t)) all = all.Where(e => e.Start <= t).ToList();

            var entries = all.OrderByDescending(e => e.Start)
                .Select(e => new { e.Id, e.Project, e.Start, e.End, e.Minutes, e.Note, e.Source })
                .ToList();

            return Results.Ok(new { totalMinutes = entries.Sum(e => e.Minutes), entries });
        });

        g.MapDelete("/time-entries/{id:guid}", async (
            Guid id, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();
            var e = await db.TimeEntries.FindAsync(id);
            if (e is null) return Results.NotFound();
            db.TimeEntries.Remove(e);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // Ticket récapitulatif d'un jour : activité auto par poste + temps saisi manuellement.
        g.MapGet("/recap", async (
            string? day, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();
            var d = DateOnly.TryParse(day, out var p) ? p : DateOnly.FromDateTime(DateTime.Now);

            var acts = await db.Activity.Where(a => a.Day == d).ToListAsync();
            var ids = acts.Select(a => a.MachineId).Distinct().ToList();
            var names = await db.Machines.Where(m => ids.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Name);

            var machines = acts.GroupBy(a => a.MachineId).Select(g => new
            {
                name = names.GetValueOrDefault(g.Key, "?"),
                totalSeconds = g.Sum(x => x.Seconds),
                apps = g.GroupBy(x => x.App)
                    .Select(ag => new { app = ag.Key, seconds = ag.Sum(x => x.Seconds) })
                    .OrderByDescending(x => x.seconds).Take(10).ToList(),
            }).OrderByDescending(m => m.totalSeconds).ToList();

            var entries = (await db.TimeEntries.ToListAsync())
                .Where(e => DateOnly.FromDateTime(e.Start.LocalDateTime) == d)
                .OrderBy(e => e.Start)
                .Select(e => new { e.Project, e.Start, e.End, e.Minutes, e.Note, e.Source })
                .ToList();

            return Results.Ok(new
            {
                day = d.ToString("yyyy-MM-dd"),
                machines,
                timeEntries = entries,
                activityTotalSeconds = acts.Sum(a => a.Seconds),
                manualTotalMinutes = entries.Sum(e => e.Minutes),
            });
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

        // Détail complet d'un poste (inventaire + bureau).
        g.MapGet("/machines/{id:guid}", async (
            Guid id, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();
            var now = DateTimeOffset.UtcNow;
            var m = await db.Machines.Include(x => x.Bureau).FirstOrDefaultAsync(x => x.Id == id);
            if (m is null) return Results.NotFound();
            return Results.Ok(new
            {
                m.Id, m.Name, m.Hostname, m.Os, m.OsVersion, m.Manufacturer, m.Model,
                m.CpuName, m.CpuCores, m.TotalRamBytes, m.PrimaryMacAddress, m.HardwareId,
                m.AgentVersion, m.EnrolledAt, m.LastSeenAt, m.CurrentUser,
                m.RemoteSessionActive, m.UserIdle, m.BureauId,
                BureauName = m.Bureau != null ? m.Bureau.Name : null,
                Online = m.LastSeenAt != null && m.LastSeenAt > now.AddMinutes(-2),
            });
        });

        // Renommer un poste.
        g.MapPatch("/machines/{id:guid}", async (
            Guid id, RenameDto dto, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(dto.Name)) return Results.BadRequest(new { error = "Nom requis." });
            var m = await db.Machines.FindAsync(id);
            if (m is null) return Results.NotFound();
            m.Name = dto.Name.Trim();
            await db.SaveChangesAsync();
            return Results.Ok(new { m.Id, m.Name });
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

            // Pour un ticket, on joint le texte à imprimer (jour courant) si non fourni.
            var payload = dto.Payload;
            if (dto.Type == AgentCommandType.PrintSummaryTicket && string.IsNullOrEmpty(payload))
                payload = await TicketService.BuildTextAsync(db, DateOnly.FromDateTime(DateTime.Now));

            var cmd = new AgentCommandRecord
            {
                MachineId = id,
                Type = dto.Type,
                Payload = payload,
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
    public record CreateTimeEntryDto(string Project, DateTimeOffset Start, DateTimeOffset End, string? Note, string? Source);
    public record RenameDto(string Name);
}

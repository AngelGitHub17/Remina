using Microsoft.EntityFrameworkCore;
using Remina.Server.Data;
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

            var code = new EnrollmentCode
            {
                Code = TokenService.NewEnrollmentCode(),
                Label = dto.Label,
                RemainingUses = dto.MaxUses <= 0 ? 1 : dto.MaxUses,
                ExpiresAt = dto.ExpiresInHours > 0
                    ? DateTimeOffset.UtcNow.AddHours(dto.ExpiresInHours)
                    : null,
            };
            db.EnrollmentCodes.Add(code);
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                code.Code, code.Label, code.RemainingUses, code.ExpiresAt,
            });
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
                    Online = m.LastSeenAt != null && m.LastSeenAt > now.AddMinutes(-2),
                })
                .ToListAsync();

            return Results.Ok(machines);
        });

        // Met une commande en file pour un poste (verrouiller, redémarrer, ticket, message...).
        g.MapPost("/machines/{id:guid}/commands", async (
            Guid id, QueueCommandDto dto, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
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

    public record CreateEnrollmentCodeDto(string? Label, int MaxUses, int ExpiresInHours);
    public record QueueCommandDto(AgentCommandType Type, string? Payload);
}

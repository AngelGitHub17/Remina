using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Remina.Server.Data;
using Remina.Server.Hubs;
using Remina.Server.Services;
using Remina.Shared;

namespace Remina.Server.Endpoints;

public static class PackageEndpoints
{
    private static string PackagesDir(IConfiguration cfg)
    {
        var dir = cfg["Remina:PackagesDir"] ?? "packages";
        if (!Path.IsPathRooted(dir)) dir = Path.Combine(Directory.GetCurrentDirectory(), dir);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void MapPackageEndpoints(this IEndpointRouteBuilder app)
    {
        // --- Admin : téléverser un installeur ---
        app.MapPost("/api/admin/packages", async (HttpRequest req, IConfiguration cfg, ReminaDbContext db) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest(new { error = "Envoi multipart attendu." });

            var form = await req.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "Fichier manquant." });

            var name = form["name"].ToString();
            Enum.TryParse<OsPlatform>(form["os"].ToString(), out var os);
            var silentArgs = form["silentArgs"].ToString();

            var pkg = new Package
            {
                Name = string.IsNullOrWhiteSpace(name) ? file.FileName : name.Trim(),
                FileName = Path.GetFileName(file.FileName),
                Os = os,
                SilentArgs = string.IsNullOrWhiteSpace(silentArgs) ? null : silentArgs.Trim(),
                SizeBytes = file.Length,
            };

            var dir = Path.Combine(PackagesDir(cfg), pkg.Id.ToString());
            Directory.CreateDirectory(dir);
            await using (var fs = File.Create(Path.Combine(dir, pkg.FileName)))
                await file.CopyToAsync(fs);

            db.Packages.Add(pkg);
            await db.SaveChangesAsync();
            return Results.Ok(new { pkg.Id, pkg.Name, pkg.FileName, pkg.Os, pkg.SilentArgs, pkg.SizeBytes });
        });

        app.MapGet("/api/admin/packages", async (HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            var list = (await db.Packages.ToListAsync())
                .OrderByDescending(p => p.UploadedAt)
                .Select(p => new { p.Id, p.Name, p.FileName, p.Os, p.SilentArgs, p.SizeBytes, p.UploadedAt });
            return Results.Ok(list);
        });

        app.MapDelete("/api/admin/packages/{id:guid}", async (
            Guid id, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            var pkg = await db.Packages.FindAsync(id);
            if (pkg is null) return Results.NotFound();
            try { Directory.Delete(Path.Combine(PackagesDir(cfg), id.ToString()), recursive: true); } catch { }
            db.Packages.Remove(pkg);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // --- Déployer un paquet sur un poste ---
        app.MapPost("/api/admin/machines/{machineId:guid}/deploy", async (
            Guid machineId, DeployDto dto, HttpContext http, IConfiguration cfg,
            ReminaDbContext db, IHubContext<AgentHub> hub) =>
        {
            var machine = await db.Machines.FindAsync(machineId);
            if (machine is null) return Results.NotFound(new { error = "Poste inconnu." });
            var pkg = await db.Packages.FindAsync(dto.PackageId);
            if (pkg is null) return Results.NotFound(new { error = "Paquet inconnu." });

            var payload = JsonSerializer.Serialize(new InstallPayload
            {
                PackageId = pkg.Id, FileName = pkg.FileName, SilentArgs = pkg.SilentArgs,
            });
            var cmd = new AgentCommandRecord
            {
                MachineId = machineId, Type = AgentCommandType.InstallPackage, Payload = payload,
            };
            db.AgentCommands.Add(cmd);
            await db.SaveChangesAsync();
            await hub.Clients.Group(machineId.ToString()).SendAsync("command", new AgentCommand
            {
                CommandId = cmd.Id, Type = cmd.Type, Payload = cmd.Payload,
            });
            return Results.Ok(new { cmd.Id });
        });

        // --- Déployer un paquet sur tous les postes d'un bureau ---
        app.MapPost("/api/admin/bureaus/{bureauId:guid}/deploy", async (
            Guid bureauId, DeployDto dto, HttpContext http, IConfiguration cfg,
            ReminaDbContext db, IHubContext<AgentHub> hub) =>
        {
            var pkg = await db.Packages.FindAsync(dto.PackageId);
            if (pkg is null) return Results.NotFound(new { error = "Paquet inconnu." });
            var machines = await db.Machines.Where(m => m.BureauId == bureauId).ToListAsync();

            var payload = JsonSerializer.Serialize(new InstallPayload
            {
                PackageId = pkg.Id, FileName = pkg.FileName, SilentArgs = pkg.SilentArgs,
            });
            foreach (var m in machines)
            {
                var cmd = new AgentCommandRecord
                {
                    MachineId = m.Id, Type = AgentCommandType.InstallPackage, Payload = payload,
                };
                db.AgentCommands.Add(cmd);
                await db.SaveChangesAsync();
                await hub.Clients.Group(m.Id.ToString()).SendAsync("command", new AgentCommand
                {
                    CommandId = cmd.Id, Type = cmd.Type, Payload = cmd.Payload,
                });
            }
            return Results.Ok(new { deployed = machines.Count });
        });

        // --- Agent : télécharger l'installeur (authentifié par jeton d'agent) ---
        app.MapGet("/api/agent/package/{id:guid}", async (
            Guid id, Guid machineId, HttpContext http, IConfiguration cfg, ReminaDbContext db) =>
        {
            var token = http.Request.Headers["X-Agent-Token"].ToString();
            var machine = await db.Machines.FirstOrDefaultAsync(m => m.Id == machineId);
            if (machine is null || string.IsNullOrEmpty(token) ||
                !TokenService.Verify(token, machine.AgentTokenHash))
                return Results.Unauthorized();

            var pkg = await db.Packages.FindAsync(id);
            if (pkg is null) return Results.NotFound();

            var path = Path.Combine(PackagesDir(cfg), id.ToString(), pkg.FileName);
            if (!File.Exists(path)) return Results.NotFound();
            return Results.File(path, "application/octet-stream", pkg.FileName);
        });
    }

    public record DeployDto(Guid PackageId);
}

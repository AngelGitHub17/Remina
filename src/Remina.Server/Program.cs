using Microsoft.EntityFrameworkCore;
using Remina.Server.Data;
using Remina.Server.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Permet de tourner comme service Windows ou unité systemd.
builder.Host.UseWindowsService(o => o.ServiceName = "Remina Server");
builder.Host.UseSystemd();

// Base de données : SQLite par défaut (simple à déployer sur Proxmox).
// Pour PostgreSQL plus tard : remplacer UseSqlite par UseNpgsql + la chaîne de connexion.
var conn = builder.Configuration.GetConnectionString("Remina")
           ?? "Data Source=remina.db";
builder.Services.AddDbContext<ReminaDbContext>(o => o.UseSqlite(conn));

builder.Services.AddEndpointsApiExplorer();

// Auto-mise à jour : fournisseur d'info (Releases GitHub) + vérificateur en tâche de fond.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<Remina.Server.Services.UpdateProvider>();
builder.Services.AddHostedService<Remina.Server.Services.ServerSelfUpdater>();

var app = builder.Build();

// Création/migration du schéma au démarrage (suffisant pour la v0).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ReminaDbContext>();
    db.Database.EnsureCreated();
}

app.MapGet("/", () => Results.Ok(new
{
    product = "Remina Server",
    version = Remina.Shared.ReminaVersion.Full,
}));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapAgentEndpoints();
app.MapAdminEndpoints();
app.MapUpdateEndpoints();

app.Run();

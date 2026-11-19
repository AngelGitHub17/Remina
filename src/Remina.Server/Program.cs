using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Remina.Server.Data;
using Remina.Server.Endpoints;
using Remina.Server.Services;

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
builder.Services.AddSignalR(o => o.MaximumReceiveMessageSize = 1024 * 1024);

// Autorise le téléversement de gros installeurs.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 4L * 1024 * 1024 * 1024);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 4L * 1024 * 1024 * 1024;
});

// Auto-mise à jour : fournisseur d'info (Releases GitHub) + vérificateur en tâche de fond.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<Remina.Server.Services.UpdateProvider>();
builder.Services.AddHostedService<Remina.Server.Services.ServerSelfUpdater>();
builder.Services.AddHostedService<Remina.Server.Services.RecapScheduler>();

// Comptes de console : service d'authentification (sessions cookie + clé historique).
builder.Services.AddScoped<AuthService>();

// Derrière un reverse proxy (Nginx, NPM) : on ne fait confiance aux en-têtes X-Forwarded-*
// que s'ils viennent d'un proxy déclaré (Remina:TrustedProxies, IP ou CIDR ; boucle locale
// toujours acceptée). Indispensable pour l'IP réelle et le cookie « Secure » en HTTPS.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    foreach (var entry in builder.Configuration.GetSection("Remina:TrustedProxies").Get<string[]>() ?? [])
    {
        var v = entry.Trim();
        if (v.Contains('/'))
        {
            var parts = v.Split('/');
            if (IPAddress.TryParse(parts[0], out var net) && int.TryParse(parts[1], out var len))
                o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(net, len));
        }
        else if (IPAddress.TryParse(v, out var ip)) o.KnownProxies.Add(ip);
    }
});

// Anti force brute : 10 tentatives de connexion par minute et par adresse IP.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.LoginRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

// Création/migration du schéma au démarrage (suffisant pour la v0).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ReminaDbContext>();
    db.Database.EnsureCreated();
    // Ajoute les tables/colonnes manquantes sur une base créée par une version antérieure.
    Remina.Server.Data.SchemaGuard.Ensure(db);

    // Recours : « Remina.Server --reset-admin <identifiant> » (à lancer sur le serveur, depuis
    // le dossier d'installation) crée/réinitialise un compte admin puis quitte.
    var i = Array.IndexOf(args, "--reset-admin");
    if (i >= 0)
    {
        if (i + 1 >= args.Length) { Console.Error.WriteLine("Usage : Remina.Server --reset-admin <identifiant>"); return 2; }
        return AuthEndpoints.ResetAdminFromCli(db, args[i + 1]);
    }

    await scope.ServiceProvider.GetRequiredService<AuthService>().PurgeSessionsAsync();
}

app.UseForwardedHeaders();

// En-têtes de sécurité : pas d'intégration en iframe, pas de sniffing MIME, referrer limité.
app.Use(async (http, next) =>
{
    var h = http.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "same-origin";
    h["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (http.Request.Path.StartsWithSegments("/api")) h["Cache-Control"] = "no-store";
    await next();
});

// Console web d'administration (wwwroot/index.html) servie à la racine.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/version", () => Results.Ok(new
{
    product = "Remina Server",
    version = Remina.Shared.ReminaVersion.Full,
}));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.UseRateLimiter();
app.UseMiddleware<AdminAuthMiddleware>();

app.MapAgentEndpoints();
app.MapAuthEndpoints();
app.MapAdminEndpoints();
app.MapUpdateEndpoints();
app.MapPackageEndpoints();
app.MapHub<Remina.Server.Hubs.AgentHub>("/hubs/agent");

await app.RunAsync();
return 0;

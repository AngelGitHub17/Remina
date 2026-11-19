using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Remina.Server.Data;

namespace Remina.Server.Services;

/// <summary>
/// Journal d'audit : qui / quoi / quand / d'où. Les écritures de l'API admin sont journalisées
/// automatiquement par <see cref="AdminAuthMiddleware"/> ; les événements de connexion le sont
/// explicitement par les endpoints d'authentification.
/// </summary>
public sealed class AuditService
{
    private const int MaxDetails = 600;
    private static readonly Regex SecretKey = new("pass|secret|token|apikey|adminkey|key$", RegexOptions.IgnoreCase);

    private readonly ReminaDbContext _db;
    private readonly ILogger<AuditService> _log;

    public AuditService(ReminaDbContext db, ILogger<AuditService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task LogAsync(HttpContext http, string action, bool success = true,
        string? targetType = null, string? targetId = null, string? details = null,
        AdminPrincipal? who = null, string? username = null, int? statusCode = null)
    {
        try
        {
            who ??= AuthService.Current(http);
            var now = DateTimeOffset.UtcNow;
            _db.AuditLog.Add(new AuditEntry
            {
                At = now,
                AtUnixMs = now.ToUnixTimeMilliseconds(),
                UserId = who?.UserId,
                Username = Truncate(username ?? who?.Username ?? "anonyme", 80)!,
                Action = Truncate(action, 160)!,
                TargetType = targetType,
                TargetId = Truncate(targetId, 80),
                Details = Truncate(details, MaxDetails),
                Ip = http.Connection.RemoteIpAddress?.ToString(),
                Success = success,
                StatusCode = statusCode,
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // L'audit ne doit jamais faire échouer l'action elle-même.
            _log.LogWarning(ex, "Audit : écriture impossible ({Action}).", action);
        }
    }

    /// <summary>
    /// Corps JSON expurgé : toute clé évoquant un secret est masquée, ainsi que la charge
    /// d'une commande d'auto-login (type 5, contient un mot de passe de session).
    /// </summary>
    public static string? Redact(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            var node = JsonNode.Parse(body);
            if (node is JsonObject o && o.TryGetPropertyValue("type", out var t)
                && t is JsonValue v && v.TryGetValue<int>(out var type) && type == 5 && o.ContainsKey("payload"))
                o["payload"] = "***";
            Mask(node);
            return node?.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }
        catch (JsonException)
        {
            return null; // corps non JSON (ex. multipart) : on ne garde rien
        }
    }

    private static void Mask(JsonNode? n)
    {
        switch (n)
        {
            case JsonObject o:
                foreach (var k in o.Select(p => p.Key).ToList())
                {
                    if (SecretKey.IsMatch(k)) o[k] = "***";
                    else Mask(o[k]);
                }
                break;
            case JsonArray a:
                foreach (var c in a) Mask(c);
                break;
        }
    }

    private static string? Truncate(string? s, int max) =>
        s is null ? null : s.Length <= max ? s : s[..max] + "…";

    /// <summary>Supprime les entrées plus anciennes que la rétention (jours).</summary>
    public static Task<int> PurgeAsync(ReminaDbContext db, int retentionDays)
    {
        var limit = DateTimeOffset.UtcNow.AddDays(-retentionDays).ToUnixTimeMilliseconds();
        return db.AuditLog.Where(e => e.AtUnixMs < limit).ExecuteDeleteAsync();
    }
}

/// <summary>Tâche de fond quotidienne : purge du journal (Remina:Audit:RetentionDays, 365 j) et des sessions mortes.</summary>
public sealed class AuditPurger : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _cfg;
    private readonly ILogger<AuditPurger> _log;

    public AuditPurger(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<AuditPurger> log)
    {
        _scopes = scopes;
        _cfg = cfg;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var days = Math.Max(1, _cfg.GetValue("Remina:Audit:RetentionDays", 365));
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ReminaDbContext>();
                var n = await AuditService.PurgeAsync(db, days);
                if (n > 0) _log.LogInformation("Audit : {N} entrée(s) de plus de {Days} j purgée(s).", n, days);
                await scope.ServiceProvider.GetRequiredService<AuthService>().PurgeSessionsAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Audit : purge impossible.");
            }
            await Task.Delay(TimeSpan.FromHours(24), ct);
        }
    }
}

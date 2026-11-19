using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Remina.Server.Data;

namespace Remina.Server.Services;

/// <summary>Identité authentifiée d'une requête de console.</summary>
/// <param name="Method">« session » (cookie) ou « legacy-key » (en-tête X-Admin-Key).</param>
public sealed record AdminPrincipal(
    Guid? UserId, string Username, string DisplayName, AdminRole Role, string Method, Guid? SessionId)
{
    public bool IsAdmin => Role == AdminRole.Admin;
}

/// <summary>
/// Authentification de la console : sessions par cookie (jeton opaque, seul son hachage est
/// stocké), et clé historique X-Admin-Key tant qu'elle n'est pas désactivée.
/// </summary>
public sealed class AuthService
{
    public const string CookieName = "remina_session";
    public const string LegacyHeader = "X-Admin-Key";
    public const string SettingLegacyKey = "LegacyKeyEnabled";
    /// <summary>Valeur d'exemple livrée dans appsettings.json : refusée hors connexion locale.</summary>
    public const string DefaultKey = "change-me-admin-key";
    private const string ItemKey = "remina.principal";

    private readonly ReminaDbContext _db;
    private readonly IConfiguration _cfg;

    public AuthService(ReminaDbContext db, IConfiguration cfg)
    {
        _db = db;
        _cfg = cfg;
    }

    private TimeSpan IdleTimeout => TimeSpan.FromHours(_cfg.GetValue("Remina:Auth:IdleHours", 12.0));
    private TimeSpan AbsoluteLifetime => TimeSpan.FromDays(_cfg.GetValue("Remina:Auth:SessionDays", 7.0));

    public static AdminPrincipal? Current(HttpContext http) =>
        http.Items.TryGetValue(ItemKey, out var p) ? p as AdminPrincipal : null;

    public static void SetCurrent(HttpContext http, AdminPrincipal p) => http.Items[ItemKey] = p;

    public static string NormalizeUsername(string? u) => (u ?? "").Trim().ToLowerInvariant();

    public static string? ValidateUsername(string u) =>
        u.Length is < 2 or > 64 || !u.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '@')
            ? "Identifiant : 2 à 64 caractères (lettres, chiffres, . _ - @)."
            : null;

    public Task<bool> AnyUserAsync() => _db.AdminUsers.AnyAsync();

    // --- Clé historique -------------------------------------------------------------------

    private string? ConfiguredKey => _cfg["Admin:ApiKey"];

    /// <summary>La clé est forcée hors service par la configuration (Admin:LegacyKeyEnabled=false).</summary>
    public bool LegacyKeyForcedOff => _cfg.GetValue("Admin:LegacyKeyEnabled", true) == false;

    public bool LegacyKeyConfigured => !string.IsNullOrEmpty(ConfiguredKey);

    public async Task<bool> LegacyKeyEnabledAsync()
    {
        if (!LegacyKeyConfigured || LegacyKeyForcedOff) return false;
        var s = await _db.ServerSettings.FindAsync(SettingLegacyKey);
        return s is null || !string.Equals(s.Value, "false", StringComparison.OrdinalIgnoreCase);
    }

    public async Task SetLegacyKeyEnabledAsync(bool enabled)
    {
        var s = await _db.ServerSettings.FindAsync(SettingLegacyKey);
        if (s is null) _db.ServerSettings.Add(new ServerSetting { Key = SettingLegacyKey, Value = enabled ? "true" : "false" });
        else s.Value = enabled ? "true" : "false";
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Compare la clé fournie à la clé configurée (temps constant). La clé d'exemple livrée par
    /// défaut n'est acceptée que depuis la machine elle-même.
    /// </summary>
    public bool LegacyKeyMatches(string? given, HttpContext http)
    {
        var expected = ConfiguredKey;
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(given)) return false;
        if (expected == DefaultKey && !IsLoopback(http)) return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(given));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static bool IsLoopback(HttpContext http) =>
        http.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);

    // --- Authentification d'une requête ---------------------------------------------------

    public async Task<AdminPrincipal?> AuthenticateAsync(HttpContext http)
    {
        var token = http.Request.Cookies[CookieName];
        if (!string.IsNullOrEmpty(token))
        {
            var p = await FromSessionAsync(token, http);
            if (p is not null) return p;
        }

        var key = http.Request.Headers[LegacyHeader].ToString();
        if (!string.IsNullOrEmpty(key) && await LegacyKeyEnabledAsync() && LegacyKeyMatches(key, http))
            return new AdminPrincipal(null, "clé-historique", "Clé d'administration", AdminRole.Admin, "legacy-key", null);

        return null;
    }

    private async Task<AdminPrincipal?> FromSessionAsync(string token, HttpContext http)
    {
        var hash = TokenService.Hash(token);
        var s = await _db.AdminSessions.FirstOrDefaultAsync(x => x.TokenHash == hash);
        if (s is null || s.RevokedAt is not null) return null;

        // Comparaisons de dates en mémoire (SQLite ne traduit pas DateTimeOffset).
        var now = DateTimeOffset.UtcNow;
        if (s.ExpiresAt <= now || now - s.LastSeenAt > IdleTimeout) return null;

        var u = await _db.AdminUsers.FirstOrDefaultAsync(x => x.Id == s.UserId);
        if (u is null || u.IsDisabled) return null;

        // Prolongation glissante, écrite au plus toutes les 5 minutes.
        if (now - s.LastSeenAt > TimeSpan.FromMinutes(5))
        {
            s.LastSeenAt = now;
            await _db.SaveChangesAsync();
        }
        return new AdminPrincipal(u.Id, u.Username, u.DisplayName, u.Role, "session", s.Id);
    }

    // --- Sessions -------------------------------------------------------------------------

    public async Task CreateSessionAsync(AdminUser user, HttpContext http)
    {
        var token = TokenService.NewAgentToken(); // 32 octets aléatoires, base64url
        var now = DateTimeOffset.UtcNow;
        var ua = http.Request.Headers.UserAgent.ToString();
        _db.AdminSessions.Add(new AdminSession
        {
            TokenHash = TokenService.Hash(token),
            UserId = user.Id,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + AbsoluteLifetime,
            Ip = http.Connection.RemoteIpAddress?.ToString(),
            UserAgent = ua.Length > 300 ? ua[..300] : ua,
        });
        user.LastLoginAt = now;
        user.FailedLogins = 0;
        user.LockoutUntil = null;
        await _db.SaveChangesAsync();

        http.Response.Cookies.Append(CookieName, token, CookieOptions(http, now + AbsoluteLifetime));
    }

    public async Task RevokeCurrentAsync(HttpContext http)
    {
        var token = http.Request.Cookies[CookieName];
        if (!string.IsNullOrEmpty(token))
        {
            var hash = TokenService.Hash(token);
            var s = await _db.AdminSessions.FirstOrDefaultAsync(x => x.TokenHash == hash);
            if (s is not null && s.RevokedAt is null)
            {
                s.RevokedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync();
            }
        }
        http.Response.Cookies.Delete(CookieName, CookieOptions(http, null));
    }

    /// <summary>Révoque toutes les sessions d'un compte (sauf éventuellement une).</summary>
    public async Task<int> RevokeAllAsync(Guid userId, Guid? except = null)
    {
        var list = await _db.AdminSessions
            .Where(x => x.UserId == userId && x.RevokedAt == null && x.Id != except)
            .ToListAsync();
        var now = DateTimeOffset.UtcNow;
        foreach (var s in list) s.RevokedAt = now;
        await _db.SaveChangesAsync();
        return list.Count;
    }

    /// <summary>Supprime les sessions expirées ou révoquées depuis plus d'un jour.</summary>
    public async Task PurgeSessionsAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var all = await _db.AdminSessions.ToListAsync();
        var dead = all.Where(s => s.ExpiresAt < now
                                  || (s.RevokedAt is not null && now - s.RevokedAt > TimeSpan.FromDays(1))
                                  || now - s.LastSeenAt > IdleTimeout + TimeSpan.FromDays(1)).ToList();
        if (dead.Count == 0) return;
        _db.AdminSessions.RemoveRange(dead);
        await _db.SaveChangesAsync();
    }

    private static CookieOptions CookieOptions(HttpContext http, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        // Secure dès que la requête arrive en HTTPS (directement ou via un proxy de confiance).
        Secure = http.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };
}

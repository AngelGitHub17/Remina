using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Remina.Server.Data;
using Remina.Server.Services;

namespace Remina.Server.Endpoints;

/// <summary>
/// Connexion via NounoursAccount (SSO maison des sites DidiKaria).
/// <code>
/// /api/auth/nounours/start?mode=login|link  → redirection vers didikaria.fr/connexion.php
/// /api/auth/nounours/callback?state&amp;sso_token  → échange serveur à serveur, session Remina
/// /api/auth/me/nounours/unlink (POST)        → délier le compte
/// </code>
/// Accès réservé aux comptes NounoursAccount de rôle ≥ MinRoleCode (admins par défaut). Un
/// compte inconnu est créé avec DefaultRole (lecture seule) ; « link » rattache le compte
/// NounoursAccount au compte Remina connecté.
/// </summary>
public static class NounoursEndpoints
{
    private const string StateCookie = "remina_na_state";
    private const string StatePath = "/api/auth/nounours";
    private sealed record PendingState(string Mode, Guid? UserId);

    public static void MapNounoursEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/nounours/start", async (string? mode, HttpContext http, NounoursService na,
            AuthService auth, IMemoryCache cache) =>
        {
            if (!na.Enabled) return Results.Redirect("/?na_error=disabled");
            Guid? userId = null;
            if (mode == "link")
            {
                var me = await auth.AuthenticateAsync(http);
                if (me?.UserId is null || me.Method != "session") return Results.Redirect("/?na_error=session");
                userId = me.UserId;
            }

            var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            cache.Set("na_state:" + state, new PendingState(mode == "link" ? "link" : "login", userId),
                TimeSpan.FromMinutes(10));
            // SameSite=Lax : le retour depuis didikaria.fr est une navigation inter-sites (GET).
            http.Response.Cookies.Append(StateCookie, state, new CookieOptions
            {
                HttpOnly = true, Secure = http.Request.IsHttps, SameSite = SameSiteMode.Lax,
                Path = StatePath, MaxAge = TimeSpan.FromMinutes(10), IsEssential = true,
            });
            return Results.Redirect(na.LoginUrl(state));
        });

        app.MapGet("/api/auth/nounours/callback", async (string? state, string? sso_token, HttpContext http,
            NounoursService na, AuthService auth, AuditService audit, IMemoryCache cache, ReminaDbContext db) =>
        {
            var cookie = http.Request.Cookies[StateCookie];
            http.Response.Cookies.Delete(StateCookie, new CookieOptions { Path = StatePath });

            async Task<IResult> Fail(string code, string detail, string? who = null)
            {
                await audit.LogAsync(http, "auth.nounours", success: false, username: who ?? "nounours", details: detail);
                return Results.Redirect("/?na_error=" + code);
            }

            if (!na.Enabled) return Results.Redirect("/?na_error=disabled");
            if (string.IsNullOrEmpty(state) || string.IsNullOrEmpty(cookie)
                || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(state), System.Text.Encoding.ASCII.GetBytes(cookie))
                || !cache.TryGetValue("na_state:" + state, out PendingState? pending) || pending is null)
                return await Fail("state", "état de connexion invalide ou expiré");
            cache.Remove("na_state:" + state);
            if (string.IsNullOrEmpty(sso_token)) return await Fail("token", "aucun jeton reçu");

            NounoursService.NaUser? nu;
            try { nu = await na.ExchangeAsync(sso_token, http.RequestAborted); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                return await Fail("unavailable", "NounoursAccount injoignable : " + ex.Message);
            }
            if (nu is null) return await Fail("token", "jeton refusé par NounoursAccount");

            var who = "na:" + nu.Username;
            var externalId = nu.Id.ToString();
            if (nu.RoleCode < na.MinRoleCode)
            {
                await na.RevokeAsync(nu.Link);
                return await Fail("denied", $"rôle NounoursAccount {nu.RoleCode} < {na.MinRoleCode}", who);
            }

            var linkedUser = await db.AdminUsers.FirstOrDefaultAsync(x =>
                x.ExternalProvider == NounoursService.Provider && x.ExternalId == externalId);

            AdminUser user;
            if (pending.Mode == "link")
            {
                var target = await db.AdminUsers.FirstOrDefaultAsync(x => x.Id == pending.UserId);
                if (target is null || target.IsDisabled) { await na.RevokeAsync(nu.Link); return await Fail("session", "compte Remina introuvable", who); }
                if (linkedUser is not null && linkedUser.Id != target.Id)
                {
                    await na.RevokeAsync(nu.Link);
                    return await Fail("conflict", $"compte NounoursAccount déjà lié à « {linkedUser.Username} »", who);
                }
                target.ExternalProvider = NounoursService.Provider;
                target.ExternalId = externalId;
                await db.SaveChangesAsync();
                user = target;
                await audit.LogAsync(http, "auth.nounours.link", who: Principal(user), targetType: "users",
                    targetId: user.Id.ToString(), details: "NounoursAccount " + nu.Username);
            }
            else if (linkedUser is not null)
            {
                user = linkedUser;
                if (user.IsDisabled) { await na.RevokeAsync(nu.Link); return await Fail("account_disabled", "compte Remina désactivé", who); }
            }
            else
            {
                user = new AdminUser
                {
                    Username = await FreeUsernameAsync(db, nu),
                    DisplayName = string.IsNullOrWhiteSpace(nu.DisplayName) ? nu.Username : nu.DisplayName.Trim(),
                    PasswordHash = null, // compte SSO uniquement (un mot de passe local peut être ajouté ensuite)
                    Role = na.DefaultRole,
                    ExternalProvider = NounoursService.Provider,
                    ExternalId = externalId,
                };
                db.AdminUsers.Add(user);
                await db.SaveChangesAsync();
                await audit.LogAsync(http, "auth.nounours.create", who: Principal(user), targetType: "users",
                    targetId: user.Id.ToString(), details: $"NounoursAccount {nu.Username} · rôle {user.Role}");
            }

            await auth.CreateSessionAsync(user, http, externalLink: nu.Link);
            await audit.LogAsync(http, "auth.login", who: Principal(user), details: "via NounoursAccount");
            return Results.Redirect("/");
        });

        app.MapPost("/api/auth/me/nounours/unlink", async (HttpContext http, AuthService auth, AuditService audit,
            ReminaDbContext db) =>
        {
            var me = await auth.AuthenticateAsync(http);
            if (me?.UserId is null || me.Method != "session") return Results.Unauthorized();
            var user = await db.AdminUsers.FirstAsync(x => x.Id == me.UserId);
            if (user.ExternalProvider != NounoursService.Provider) return Results.BadRequest(new { error = "Compte non lié." });
            if (user.PasswordHash is null)
                return Results.BadRequest(new { error = "Définis d'abord un mot de passe local, sinon tu ne pourras plus te connecter." });
            user.ExternalProvider = null;
            user.ExternalId = null;
            await db.SaveChangesAsync();
            await audit.LogAsync(http, "auth.nounours.unlink", who: me, targetType: "users", targetId: user.Id.ToString());
            return Results.Ok();
        });
    }

    /// <summary>Identifiant local : pseudo NounoursAccount s'il est valide et libre, sinon « na-&lt;id&gt; ».</summary>
    private static async Task<string> FreeUsernameAsync(ReminaDbContext db, NounoursService.NaUser nu)
    {
        var candidate = AuthService.NormalizeUsername(nu.Username);
        if (AuthService.ValidateUsername(candidate) is null && !await db.AdminUsers.AnyAsync(x => x.Username == candidate))
            return candidate;
        var fallback = "na-" + nu.Id;
        var n = 1;
        var name = fallback;
        while (await db.AdminUsers.AnyAsync(x => x.Username == name)) name = $"{fallback}-{++n}";
        return name;
    }

    private static AdminPrincipal Principal(AdminUser u) =>
        new(u.Id, u.Username, u.DisplayName, u.Role, "session", null);
}

using Microsoft.EntityFrameworkCore;
using Remina.Server.Data;
using Remina.Server.Services;

namespace Remina.Server.Endpoints;

/// <summary>
/// Connexion à la console (<c>/api/auth/*</c>), gestion des comptes et réglages de sécurité
/// (<c>/api/admin/users</c>, <c>/api/admin/settings</c> — rôle admin, voir AdminAuthMiddleware).
/// </summary>
public static class AuthEndpoints
{
    public const string LoginRateLimit = "auth-login";
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var a = app.MapGroup("/api/auth");

        // État de la console : première configuration à faire ? qui suis-je ?
        a.MapGet("/status", async (HttpContext http, AuthService auth) =>
        {
            var any = await auth.AnyUserAsync();
            var me = await auth.AuthenticateAsync(http);
            return Results.Ok(new
            {
                setupRequired = !any,
                legacyKeyEnabled = await auth.LegacyKeyEnabledAsync(),
                user = me is null ? null : MeDto(me),
            });
        });

        // Création du premier compte admin : preuve = clé d'administration de la configuration.
        a.MapPost("/bootstrap", async (BootstrapDto dto, HttpContext http, AuthService auth, AuditService audit, ReminaDbContext db) =>
        {
            if (await auth.AnyUserAsync())
                return Results.Conflict(new { error = "Un compte existe déjà : connecte-toi." });
            if (!auth.LegacyKeyConfigured)
                return Results.BadRequest(new { error = "Aucune clé Admin:ApiKey configurée sur le serveur. Utilise --reset-admin <identifiant> en ligne de commande." });
            if (!auth.LegacyKeyMatches(dto.AdminKey, http))
            {
                await audit.LogAsync(http, "auth.bootstrap", success: false, username: Clip(dto.Username), statusCode: 403);
                return Results.Json(new { error = "Clé d'administration refusée." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var username = AuthService.NormalizeUsername(dto.Username);
            var err = AuthService.ValidateUsername(username) ?? PasswordHasher.Validate(dto.Password);
            if (err is not null) return Results.BadRequest(new { error = err });

            var user = new AdminUser
            {
                Username = username,
                DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? username : dto.DisplayName.Trim(),
                PasswordHash = PasswordHasher.Hash(dto.Password!),
                Role = AdminRole.Admin,
            };
            db.AdminUsers.Add(user);
            await db.SaveChangesAsync();
            await auth.CreateSessionAsync(user, http);
            await audit.LogAsync(http, "auth.bootstrap", who: Principal(user), targetType: "users", targetId: user.Id.ToString());
            return Results.Ok(new { user = UserDto(user) });
        }).RequireRateLimiting(LoginRateLimit);

        a.MapPost("/login", async (LoginDto dto, HttpContext http, AuthService auth, AuditService audit, ReminaDbContext db) =>
        {
            var username = AuthService.NormalizeUsername(dto.Username);
            var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
            var now = DateTimeOffset.UtcNow;
            const string generic = "Identifiant ou mot de passe incorrect.";

            if (user is null)
            {
                PasswordHasher.Verify(dto.Password ?? "", null); // temps de réponse homogène
                await audit.LogAsync(http, "auth.login", success: false, username: Clip(username), details: "identifiant inconnu", statusCode: 401);
                return Results.Json(new { error = generic }, statusCode: StatusCodes.Status401Unauthorized);
            }
            if (user.LockoutUntil is { } until && until > now)
            {
                await audit.LogAsync(http, "auth.login", success: false, who: Principal(user), details: "compte verrouillé", statusCode: 423);
                return Results.Json(new { error = $"Compte verrouillé après trop d'échecs. Réessaie après {until.ToLocalTime():HH:mm}." },
                    statusCode: StatusCodes.Status423Locked);
            }

            if (!PasswordHasher.Verify(dto.Password ?? "", user.PasswordHash))
            {
                user.FailedLogins++;
                if (user.FailedLogins >= MaxFailedLogins)
                {
                    user.LockoutUntil = now + LockoutDuration;
                    user.FailedLogins = 0;
                }
                await db.SaveChangesAsync();
                await audit.LogAsync(http, "auth.login", success: false, who: Principal(user),
                    details: user.LockoutUntil is not null ? "mot de passe incorrect — compte verrouillé" : "mot de passe incorrect", statusCode: 401);
                return Results.Json(new { error = generic }, statusCode: StatusCodes.Status401Unauthorized);
            }
            if (user.IsDisabled)
            {
                await audit.LogAsync(http, "auth.login", success: false, who: Principal(user), details: "compte désactivé", statusCode: 403);
                return Results.Json(new { error = "Compte désactivé." }, statusCode: StatusCodes.Status403Forbidden);
            }

            if (PasswordHasher.NeedsRehash(user.PasswordHash!))
                user.PasswordHash = PasswordHasher.Hash(dto.Password!);
            await auth.CreateSessionAsync(user, http);
            await audit.LogAsync(http, "auth.login", who: Principal(user));
            return Results.Ok(new { user = UserDto(user) });
        }).RequireRateLimiting(LoginRateLimit);

        a.MapPost("/logout", async (HttpContext http, AuthService auth, AuditService audit) =>
        {
            var me = await auth.AuthenticateAsync(http);
            await auth.RevokeCurrentAsync(http);
            if (me is not null) await audit.LogAsync(http, "auth.logout", who: me);
            return Results.Ok();
        });

        a.MapGet("/me", async (HttpContext http, AuthService auth) =>
        {
            var me = await auth.AuthenticateAsync(http);
            return me is null ? Results.Unauthorized() : Results.Ok(MeDto(me));
        });

        // Changement de son propre mot de passe : les autres sessions sont révoquées.
        a.MapPost("/me/password", async (ChangePasswordDto dto, HttpContext http, AuthService auth, AuditService audit, ReminaDbContext db) =>
        {
            var me = await auth.AuthenticateAsync(http);
            if (me?.UserId is null || me.Method != "session") return Results.Unauthorized();
            var user = await db.AdminUsers.FirstAsync(u => u.Id == me.UserId);
            if (user.PasswordHash is not null && !PasswordHasher.Verify(dto.CurrentPassword ?? "", user.PasswordHash))
            {
                await audit.LogAsync(http, "auth.password.change", success: false, who: me, statusCode: 400);
                return Results.BadRequest(new { error = "Mot de passe actuel incorrect." });
            }
            var err = PasswordHasher.Validate(dto.NewPassword);
            if (err is not null) return Results.BadRequest(new { error = err });
            user.PasswordHash = PasswordHasher.Hash(dto.NewPassword!);
            await db.SaveChangesAsync();
            await auth.RevokeAllAsync(user.Id, except: me.SessionId);
            await audit.LogAsync(http, "auth.password.change", who: me);
            return Results.Ok();
        }).RequireRateLimiting(LoginRateLimit);

        // --- Jetons d'API personnels (scripts) : gérés depuis une session uniquement -----------
        a.MapGet("/me/tokens", async (HttpContext http, AuthService auth, ReminaDbContext db) =>
        {
            var me = await auth.AuthenticateAsync(http);
            if (me?.UserId is null || me.Method != "session") return Results.Unauthorized();
            var list = (await db.ApiTokens.Where(t => t.UserId == me.UserId && t.RevokedAt == null).ToListAsync())
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new { t.Id, t.Name, t.Prefix, t.CreatedAt, t.LastUsedAt, t.ExpiresAt,
                    Expired = t.ExpiresAt is { } e && e <= DateTimeOffset.UtcNow });
            return Results.Ok(list);
        });

        a.MapPost("/me/tokens", async (CreateTokenDto dto, HttpContext http, AuthService auth, AuditService audit) =>
        {
            var me = await auth.AuthenticateAsync(http);
            if (me?.UserId is null || me.Method != "session") return Results.Unauthorized();
            var name = (dto.Name ?? "").Trim();
            if (name.Length is < 1 or > 60) return Results.BadRequest(new { error = "Nom du jeton : 1 à 60 caractères." });
            if (dto.ExpiresInDays is < 0 or > 3650) return Results.BadRequest(new { error = "Durée invalide." });
            var (t, raw) = await auth.CreateApiTokenAsync(me.UserId.Value, name, dto.ExpiresInDays);
            await audit.LogAsync(http, "token.create", who: me, targetType: "tokens", targetId: t.Id.ToString(), details: name);
            return Results.Ok(new { t.Id, t.Name, t.Prefix, t.ExpiresAt, token = raw });
        });

        a.MapDelete("/me/tokens/{id:guid}", async (Guid id, HttpContext http, AuthService auth, AuditService audit, ReminaDbContext db) =>
        {
            var me = await auth.AuthenticateAsync(http);
            if (me?.UserId is null || me.Method != "session") return Results.Unauthorized();
            var t = await db.ApiTokens.FirstOrDefaultAsync(x => x.Id == id && x.UserId == me.UserId);
            if (t is null) return Results.NotFound();
            t.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await audit.LogAsync(http, "token.revoke", who: me, targetType: "tokens", targetId: id.ToString(), details: t.Name);
            return Results.Ok();
        });

        // --- Journal d'audit (admin) : filtres + pagination par curseur (before = AtUnixMs) ---
        app.MapGet("/api/admin/audit", async (ReminaDbContext db, string? user, string? action,
            string? from, string? to, bool? failedOnly, long? before, int? limit) =>
        {
            var q = db.AuditLog.AsQueryable();
            if (!string.IsNullOrWhiteSpace(user)) q = q.Where(e => e.Username.Contains(user.Trim().ToLower()));
            if (!string.IsNullOrWhiteSpace(action)) q = q.Where(e => e.Action.Contains(action.Trim()));
            if (failedOnly == true) q = q.Where(e => !e.Success);
            if (DateTimeOffset.TryParse(from, out var f)) { var ms = f.ToUnixTimeMilliseconds(); q = q.Where(e => e.AtUnixMs >= ms); }
            if (DateTimeOffset.TryParse(to, out var t)) { var ms = t.ToUnixTimeMilliseconds(); q = q.Where(e => e.AtUnixMs <= ms); }
            if (before is { } b) q = q.Where(e => e.AtUnixMs < b);
            var n = Math.Clamp(limit ?? 100, 1, 500);
            // Tri sur l'entier AtUnixMs : traduisible en SQL (contrairement à DateTimeOffset).
            var rows = await q.OrderByDescending(e => e.AtUnixMs).Take(n + 1).ToListAsync();
            var more = rows.Count > n;
            var page = rows.Take(n).ToList();
            return Results.Ok(new
            {
                entries = page.Select(e => new { e.Id, e.At, e.Username, e.Action, e.TargetType, e.TargetId,
                    e.Details, e.Ip, e.Success, e.StatusCode }),
                next = more ? page[^1].AtUnixMs : (long?)null,
            });
        });

        // --- Comptes (admin) -----------------------------------------------------------------
        var u = app.MapGroup("/api/admin/users");

        u.MapGet("", async (ReminaDbContext db) =>
        {
            var users = await db.AdminUsers.ToListAsync();
            var now = DateTimeOffset.UtcNow;
            var sessions = (await db.AdminSessions.Where(s => s.RevokedAt == null).ToListAsync())
                .Where(s => s.ExpiresAt > now)
                .GroupBy(s => s.UserId).ToDictionary(g => g.Key, g => g.Count());
            return Results.Ok(users.OrderBy(x => x.Username).Select(x => new
            {
                x.Id, x.Username, x.DisplayName, x.Role, x.IsDisabled, x.CreatedAt, x.LastLoginAt,
                Locked = x.LockoutUntil is { } l && l > now,
                HasPassword = x.PasswordHash is not null,
                x.ExternalProvider,
                ActiveSessions = sessions.GetValueOrDefault(x.Id),
            }));
        });

        u.MapPost("", async (CreateUserDto dto, ReminaDbContext db) =>
        {
            var username = AuthService.NormalizeUsername(dto.Username);
            var err = AuthService.ValidateUsername(username) ?? PasswordHasher.Validate(dto.Password);
            if (err is not null) return Results.BadRequest(new { error = err });
            if (!Enum.IsDefined(dto.Role)) return Results.BadRequest(new { error = "Rôle inconnu." });
            if (await db.AdminUsers.AnyAsync(x => x.Username == username))
                return Results.Conflict(new { error = "Cet identifiant existe déjà." });

            var user = new AdminUser
            {
                Username = username,
                DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? username : dto.DisplayName.Trim(),
                PasswordHash = PasswordHasher.Hash(dto.Password!),
                Role = dto.Role,
            };
            db.AdminUsers.Add(user);
            await db.SaveChangesAsync();
            return Results.Ok(UserDto(user));
        });

        u.MapPut("/{id:guid}", async (Guid id, UpdateUserDto dto, HttpContext http, AuthService auth, ReminaDbContext db) =>
        {
            var user = await db.AdminUsers.FindAsync(id);
            if (user is null) return Results.NotFound();
            var role = dto.Role ?? user.Role;
            var disabled = dto.IsDisabled ?? user.IsDisabled;
            if (!Enum.IsDefined(role)) return Results.BadRequest(new { error = "Rôle inconnu." });

            var me = AuthService.Current(http);
            if (me?.UserId == id && (disabled || role != AdminRole.Admin))
                return Results.BadRequest(new { error = "Tu ne peux pas retirer tes propres droits admin ni te désactiver." });
            if ((disabled || role != AdminRole.Admin) && await IsLastActiveAdminAsync(db, user))
                return Results.BadRequest(new { error = "Il doit rester au moins un compte admin actif." });

            if (!string.IsNullOrWhiteSpace(dto.DisplayName)) user.DisplayName = dto.DisplayName.Trim();
            user.Role = role;
            user.IsDisabled = disabled;
            if (dto.Unlock == true) { user.LockoutUntil = null; user.FailedLogins = 0; }
            await db.SaveChangesAsync();
            if (disabled) await auth.RevokeAllAsync(user.Id);
            return Results.Ok(UserDto(user));
        });

        // Réinitialisation du mot de passe d'un compte par un admin : ses sessions sont fermées.
        u.MapPost("/{id:guid}/password", async (Guid id, SetPasswordDto dto, HttpContext http, AuthService auth, ReminaDbContext db) =>
        {
            var user = await db.AdminUsers.FindAsync(id);
            if (user is null) return Results.NotFound();
            var err = PasswordHasher.Validate(dto.Password);
            if (err is not null) return Results.BadRequest(new { error = err });
            user.PasswordHash = PasswordHasher.Hash(dto.Password!);
            user.LockoutUntil = null;
            user.FailedLogins = 0;
            await db.SaveChangesAsync();
            await auth.RevokeAllAsync(user.Id, except: AuthService.Current(http)?.SessionId);
            return Results.Ok();
        });

        u.MapPost("/{id:guid}/revoke-sessions", async (Guid id, HttpContext http, AuthService auth) =>
        {
            var n = await auth.RevokeAllAsync(id, except: AuthService.Current(http)?.SessionId);
            return Results.Ok(new { revoked = n });
        });

        u.MapDelete("/{id:guid}", async (Guid id, HttpContext http, ReminaDbContext db) =>
        {
            var user = await db.AdminUsers.FindAsync(id);
            if (user is null) return Results.NotFound();
            if (AuthService.Current(http)?.UserId == id)
                return Results.BadRequest(new { error = "Tu ne peux pas supprimer ton propre compte." });
            if (await IsLastActiveAdminAsync(db, user))
                return Results.BadRequest(new { error = "Il doit rester au moins un compte admin actif." });
            db.AdminSessions.RemoveRange(await db.AdminSessions.Where(s => s.UserId == id).ToListAsync());
            db.ApiTokens.RemoveRange(await db.ApiTokens.Where(t => t.UserId == id).ToListAsync());
            db.AdminUsers.Remove(user);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // --- Réglages de sécurité (admin) ------------------------------------------------------
        var s = app.MapGroup("/api/admin/settings");

        s.MapGet("", async (AuthService auth) => Results.Ok(new
        {
            legacyKeyEnabled = await auth.LegacyKeyEnabledAsync(),
            legacyKeyConfigured = auth.LegacyKeyConfigured,
            legacyKeyForcedOff = auth.LegacyKeyForcedOff,
        }));

        s.MapPut("", async (SettingsDto dto, HttpContext http, AuthService auth, ReminaDbContext db) =>
        {
            if (dto.LegacyKeyEnabled is { } enabled)
            {
                if (!enabled && !await db.AdminUsers.AnyAsync(x => x.Role == AdminRole.Admin && !x.IsDisabled))
                    return Results.BadRequest(new { error = "Crée d'abord un compte admin actif avant de désactiver la clé." });
                if (enabled && auth.LegacyKeyForcedOff)
                    return Results.BadRequest(new { error = "La clé est désactivée par la configuration (Admin:LegacyKeyEnabled=false)." });
                await auth.SetLegacyKeyEnabledAsync(enabled);
            }
            return Results.Ok(new { legacyKeyEnabled = await auth.LegacyKeyEnabledAsync() });
        });
    }

    private static async Task<bool> IsLastActiveAdminAsync(ReminaDbContext db, AdminUser user)
    {
        if (user.Role != AdminRole.Admin || user.IsDisabled) return false;
        return !await db.AdminUsers.AnyAsync(x => x.Id != user.Id && x.Role == AdminRole.Admin && !x.IsDisabled);
    }

    /// <summary>
    /// Ligne de commande <c>--reset-admin &lt;identifiant&gt;</c> : crée le compte (admin) ou
    /// réinitialise son mot de passe, le réactive et ferme ses sessions. Affiche un mot de passe
    /// temporaire. Recours si plus aucun admin ne peut se connecter.
    /// </summary>
    public static int ResetAdminFromCli(ReminaDbContext db, string usernameArg)
    {
        var username = AuthService.NormalizeUsername(usernameArg);
        var err = AuthService.ValidateUsername(username);
        if (err is not null) { Console.Error.WriteLine(err); return 2; }

        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var password = new string(System.Security.Cryptography.RandomNumberGenerator
            .GetItems(alphabet.AsSpan(), 20));

        var user = db.AdminUsers.FirstOrDefault(x => x.Username == username);
        if (user is null)
        {
            user = new AdminUser { Username = username, DisplayName = username };
            db.AdminUsers.Add(user);
        }
        user.PasswordHash = PasswordHasher.Hash(password);
        user.Role = AdminRole.Admin;
        user.IsDisabled = false;
        user.LockoutUntil = null;
        user.FailedLogins = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var sess in db.AdminSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null))
            sess.RevokedAt = now;
        db.AuditLog.Add(new AuditEntry
        {
            At = now, AtUnixMs = now.ToUnixTimeMilliseconds(), UserId = user.Id, Username = "console-serveur",
            Action = "auth.reset-admin", TargetType = "users", TargetId = user.Id.ToString(), Details = username,
        });
        db.SaveChanges();

        Console.WriteLine($"Compte admin « {username} » prêt.");
        Console.WriteLine($"Mot de passe temporaire : {password}");
        Console.WriteLine("Change-le dès la première connexion (Mon compte).");
        return 0;
    }

    private static AdminPrincipal Principal(AdminUser u) =>
        new(u.Id, u.Username, u.DisplayName, u.Role, "session", null);

    private static string Clip(string? s) =>
        AuthService.NormalizeUsername(s) is var n && n.Length > 64 ? n[..64] : AuthService.NormalizeUsername(s);

    private static object UserDto(AdminUser x) => new { x.Id, x.Username, x.DisplayName, x.Role, x.IsDisabled };

    private static object MeDto(AdminPrincipal p) => new
    {
        p.UserId, p.Username, p.DisplayName, p.Role, p.Method, IsAdmin = p.IsAdmin,
    };

    public record BootstrapDto(string? AdminKey, string? Username, string? DisplayName, string? Password);
    public record LoginDto(string? Username, string? Password);
    public record ChangePasswordDto(string? CurrentPassword, string? NewPassword);
    public record CreateUserDto(string? Username, string? DisplayName, string? Password, AdminRole Role);
    public record UpdateUserDto(string? DisplayName, AdminRole? Role, bool? IsDisabled, bool? Unlock);
    public record SetPasswordDto(string? Password);
    public record SettingsDto(bool? LegacyKeyEnabled);
    public record CreateTokenDto(string? Name, int? ExpiresInDays);
}

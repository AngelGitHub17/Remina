using Remina.Server.Data;

namespace Remina.Server.Services;

/// <summary>
/// Protège toute l'API de console (<c>/api/admin/*</c>) : un nouvel endpoint est protégé par
/// défaut, sans vérification à recopier.
/// <list type="bullet">
/// <item>Lecture (GET/HEAD) : tout compte connecté (admin ou lecture seule).</item>
/// <item>Écriture : rôle admin, sauf la saisie de temps (autorisée en lecture seule).</item>
/// <item>Comptes, réglages et journal d'audit : rôle admin, même en lecture.</item>
/// <item>Toute écriture (réussie ou refusée) est inscrite au journal d'audit.</item>
/// <item>Anti-CSRF : une écriture authentifiée par cookie doit porter l'en-tête
/// <c>X-Remina: 1</c> (qu'un site tiers ne peut pas poser sans accord CORS).</item>
/// </list>
/// </summary>
public sealed class AdminAuthMiddleware
{
    public const string CsrfHeader = "X-Remina";
    private readonly RequestDelegate _next;

    public AdminAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext http, AuthService auth, AuditService audit)
    {
        var path = http.Request.Path;
        var isAdminApi = path.StartsWithSegments("/api/admin");
        var isAuthApi = path.StartsWithSegments("/api/auth");
        if (!isAdminApi && !isAuthApi)
        {
            await _next(http);
            return;
        }

        var method = http.Request.Method;
        var isRead = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);

        // Anti-CSRF : toute écriture navigateur passe par notre JS, qui pose l'en-tête.
        // (Les scripts utilisant X-Admin-Key ne portent pas de cookie : non concernés.)
        if (!isRead && http.Request.Cookies.ContainsKey(AuthService.CookieName)
            && http.Request.Headers[CsrfHeader] != "1")
        {
            await Deny(http, StatusCodes.Status403Forbidden, "Requête refusée (en-tête X-Remina manquant).");
            return;
        }

        if (isAuthApi)
        {
            await _next(http);
            return;
        }

        var principal = await auth.AuthenticateAsync(http);
        if (principal is null)
        {
            await Deny(http, StatusCodes.Status401Unauthorized, "Connexion requise.");
            return;
        }

        AuthService.SetCurrent(http, principal);

        if (!principal.IsAdmin && !ReadOnlyAllowed(path, method, isRead))
        {
            if (!isRead) await LogWriteAsync(http, audit, null, StatusCodes.Status403Forbidden);
            await Deny(http, StatusCodes.Status403Forbidden, "Droits insuffisants (compte en lecture seule).");
            return;
        }

        if (isRead)
        {
            await _next(http);
            return;
        }

        // Écriture : on garde le corps JSON (petit) pour le journal, expurgé des secrets.
        var body = await ReadSmallJsonBodyAsync(http);
        await _next(http);
        await LogWriteAsync(http, audit, body, http.Response.StatusCode);
    }

    private static async Task<string?> ReadSmallJsonBodyAsync(HttpContext http)
    {
        var req = http.Request;
        if (req.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true
            || req.ContentLength is null or > 16 * 1024) return null;
        req.EnableBuffering();
        using var reader = new StreamReader(req.Body, leaveOpen: true);
        var text = await reader.ReadToEndAsync();
        req.Body.Position = 0;
        return text;
    }

    private static Task LogWriteAsync(HttpContext http, AuditService audit, string? body, int status)
    {
        // Action = méthode + gabarit de route (« POST /api/admin/machines/{id:guid}/commands »).
        var pattern = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? http.Request.Path.Value ?? "";
        if (!pattern.StartsWith('/')) pattern = "/" + pattern;
        pattern = System.Text.RegularExpressions.Regex.Replace(pattern, @"\{(\w+):[^}]+\}", "{$1}");
        var rv = http.Request.RouteValues;
        var target = (rv.TryGetValue("id", out var id) ? id
            : rv.TryGetValue("machineId", out var m) ? m
            : rv.TryGetValue("bureauId", out var b) ? b : null)?.ToString();
        var targetType = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(2);
        return audit.LogAsync(http, $"{http.Request.Method} {pattern}", status < 400,
            targetType, target, AuditService.Redact(body), statusCode: status);
    }

    private static bool ReadOnlyAllowed(PathString path, string method, bool isRead)
    {
        if (path.StartsWithSegments("/api/admin/users") || path.StartsWithSegments("/api/admin/settings")
            || path.StartsWithSegments("/api/admin/audit"))
            return false;
        if (isRead) return true;
        // Saisie de temps : autorisée en lecture seule (page mobile /log.html, récap).
        return HttpMethods.IsPost(method) && path.Equals("/api/admin/time-entries", StringComparison.OrdinalIgnoreCase);
    }

    private static Task Deny(HttpContext http, int status, string error)
    {
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(new { error });
    }
}

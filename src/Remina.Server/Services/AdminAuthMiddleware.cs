using Remina.Server.Data;

namespace Remina.Server.Services;

/// <summary>
/// Protège toute l'API de console (<c>/api/admin/*</c>) : un nouvel endpoint est protégé par
/// défaut, sans vérification à recopier.
/// <list type="bullet">
/// <item>Lecture (GET/HEAD) : tout compte connecté (admin ou lecture seule).</item>
/// <item>Écriture : rôle admin, sauf la saisie de temps (autorisée en lecture seule).</item>
/// <item>Comptes et réglages : rôle admin, même en lecture.</item>
/// <item>Anti-CSRF : une écriture authentifiée par cookie doit porter l'en-tête
/// <c>X-Remina: 1</c> (qu'un site tiers ne peut pas poser sans accord CORS).</item>
/// </list>
/// </summary>
public sealed class AdminAuthMiddleware
{
    public const string CsrfHeader = "X-Remina";
    private readonly RequestDelegate _next;

    public AdminAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext http, AuthService auth)
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

        if (!principal.IsAdmin && !ReadOnlyAllowed(path, method, isRead))
        {
            await Deny(http, StatusCodes.Status403Forbidden, "Droits insuffisants (compte en lecture seule).");
            return;
        }

        AuthService.SetCurrent(http, principal);
        await _next(http);
    }

    private static bool ReadOnlyAllowed(PathString path, string method, bool isRead)
    {
        if (path.StartsWithSegments("/api/admin/users") || path.StartsWithSegments("/api/admin/settings"))
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

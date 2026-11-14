using System.Text.Json;
using Remina.Shared;

namespace Remina.Server.Services;

/// <summary>
/// Détermine la dernière version publiée et l'URL de l'archive par (application, RID),
/// à partir des Releases GitHub du dépôt. Résultat mis en cache pour ne pas marteler l'API.
/// </summary>
public class UpdateProvider
{
    private readonly HttpClient _http;
    private readonly IConfiguration _cfg;
    private readonly ILogger<UpdateProvider> _log;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;
    private string _latestTag = "";
    private readonly Dictionary<string, string> _assets = new(); // nom d'asset -> url

    public UpdateProvider(HttpClient http, IConfiguration cfg, ILogger<UpdateProvider> log)
    {
        _http = http;
        _cfg = cfg;
        _log = log;
    }

    private string Repo => _cfg["Remina:Update:Repo"] ?? "AngelGitHub17/Remina";
    private bool Enabled => _cfg.GetValue("Remina:Update:Enabled", true);
    private TimeSpan CacheTtl => TimeSpan.FromMinutes(_cfg.GetValue("Remina:Update:CacheMinutes", 30));

    public async Task<UpdateCheckResult> CheckAsync(UpdateApp app, string rid, string currentVersion, CancellationToken ct)
    {
        if (!Enabled) return new UpdateCheckResult { UpdateAvailable = false, LatestVersion = currentVersion };

        await EnsureFreshAsync(ct);
        if (string.IsNullOrEmpty(_latestTag))
            return new UpdateCheckResult { UpdateAvailable = false, LatestVersion = currentVersion };

        var appName = app == UpdateApp.Server ? "Server" : "Agent";
        var assetName = $"Remina-{appName}-{rid}.zip";
        _assets.TryGetValue(assetName, out var url);

        return new UpdateCheckResult
        {
            LatestVersion = _latestTag,
            DownloadUrl = url,
            UpdateAvailable = url is not null && SemVer.IsNewer(_latestTag, currentVersion),
        };
    }

    private async Task EnsureFreshAsync(CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow - _fetchedAt < CacheTtl && !string.IsNullOrEmpty(_latestTag))
            return;

        await _lock.WaitAsync(ct);
        try
        {
            if (DateTimeOffset.UtcNow - _fetchedAt < CacheTtl && !string.IsNullOrEmpty(_latestTag))
                return;

            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{Repo}/releases/latest");
            req.Headers.UserAgent.ParseAdd("Remina-Server");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogInformation("Pas de Release récupérable ({Code}).", (int)resp.StatusCode);
                _fetchedAt = DateTimeOffset.UtcNow; // évite de réessayer en boucle
                return;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            _latestTag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            _assets.Clear();
            if (root.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString();
                    var url = a.GetProperty("browser_download_url").GetString();
                    if (name is not null && url is not null) _assets[name] = url;
                }

            _fetchedAt = DateTimeOffset.UtcNow;
            _log.LogInformation("Dernière version publiée : {Tag} ({Count} archives).", _latestTag, _assets.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Vérification des Releases impossible.");
            _fetchedAt = DateTimeOffset.UtcNow;
        }
        finally { _lock.Release(); }
    }
}

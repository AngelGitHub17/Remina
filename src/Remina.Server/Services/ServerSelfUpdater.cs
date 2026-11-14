using Remina.Shared;

namespace Remina.Server.Services;

/// <summary>
/// Vérifie périodiquement si une version plus récente du serveur est publiée et,
/// le cas échéant, l'applique (télécharge, met en attente, bascule et redémarre).
/// Désactivable via Remina:Update:Enabled = false.
/// </summary>
public class ServerSelfUpdater : BackgroundService
{
    private readonly UpdateProvider _provider;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<ServerSelfUpdater> _log;

    public ServerSelfUpdater(UpdateProvider provider, IHttpClientFactory httpFactory,
        IConfiguration cfg, ILogger<ServerSelfUpdater> log)
    {
        _provider = provider;
        _httpFactory = httpFactory;
        _cfg = cfg;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_cfg.GetValue("Remina:Update:Enabled", true)) return;

        var every = TimeSpan.FromHours(_cfg.GetValue("Remina:Update:CheckHours", 6));
        await Task.Delay(TimeSpan.FromMinutes(1), ct); // laisse le serveur démarrer

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var res = await _provider.CheckAsync(UpdateApp.Server, Updater.CurrentRid,
                    ReminaVersion.Semver, ct);
                if (res.UpdateAvailable && res.DownloadUrl is not null)
                {
                    _log.LogInformation("Mise à jour serveur {Ver} disponible.", res.LatestVersion);
                    var http = _httpFactory.CreateClient();
                    http.Timeout = TimeSpan.FromMinutes(5);
                    await Updater.DownloadAndStageAsync(http, res.DownloadUrl,
                        AppContext.BaseDirectory, m => _log.LogInformation("{M}", m), ct);
                    Updater.LaunchHelperAndExit(AppContext.BaseDirectory, "Remina.Server",
                        ServiceName(), m => _log.LogInformation("{M}", m));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Auto-update serveur : échec de la vérification.");
            }

            await Task.Delay(every, ct);
        }
    }

    private string? ServiceName() =>
        _cfg["Remina:Update:ServiceName"]
        ?? (OperatingSystem.IsWindows() ? "ReminaServer"
            : OperatingSystem.IsLinux() ? "remina-server" : null);
}

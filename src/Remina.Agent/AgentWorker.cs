using Remina.Agent.Platform;
using Remina.Shared;

namespace Remina.Agent;

/// <summary>
/// Cœur de l'agent : inscription au premier démarrage, puis boucle de heartbeat.
/// La connexion automatique de session est suspendue tant qu'une session RDP est active.
/// </summary>
public class AgentWorker : BackgroundService
{
    private readonly ILogger<AgentWorker> _log;
    private readonly IConfiguration _cfg;
    private readonly IPlatform _platform;
    private readonly ServerClient _client;
    private AgentState _state;

    public AgentWorker(ILogger<AgentWorker> log, IConfiguration cfg)
    {
        _log = log;
        _cfg = cfg;
        _platform = PlatformFactory.Create();
        var serverUrl = cfg["Remina:ServerUrl"] ?? "http://localhost:5080";
        _client = new ServerClient(serverUrl);
        _state = AgentState.Load() ?? new AgentState();
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("Agent Remina {Version} — OS {Os}", ReminaVersion.Full, _platform.Os);

        if (!_state.IsEnrolled)
        {
            if (!await TryEnrollAsync(ct))
            {
                _log.LogWarning("Inscription impossible. Vérifie le code d'inscription et l'URL du serveur.");
                return;
            }
        }

        var idleThreshold = TimeSpan.FromMinutes(
            _cfg.GetValue("Remina:IdleMinutes", 5));
        int nextDelay = 30;

        var updateEnabled = _cfg.GetValue("Remina:Update:Enabled", true);
        var updateEvery = TimeSpan.FromHours(_cfg.GetValue("Remina:Update:CheckHours", 6));
        var nextUpdateCheck = DateTimeOffset.UtcNow.AddMinutes(1); // première vérif après 1 min

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (updateEnabled && DateTimeOffset.UtcNow >= nextUpdateCheck)
                {
                    nextUpdateCheck = DateTimeOffset.UtcNow + updateEvery;
                    await TrySelfUpdateAsync(ct); // ne revient pas si une mise à jour s'applique
                }

                var remote = _platform.IsRemoteSessionActive();
                var hb = new HeartbeatRequest
                {
                    MachineId = _state.MachineId,
                    CurrentUser = _platform.GetCurrentUser(),
                    RemoteSessionActive = remote,
                    UserIdle = _platform.IsUserIdle(idleThreshold),
                    AgentVersion = ReminaVersion.Semver,
                };

                var resp = await _client.HeartbeatAsync(_state.AgentToken, hb, ct);
                if (resp is not null)
                {
                    nextDelay = resp.NextHeartbeatSeconds;
                    foreach (var cmd in resp.Commands)
                        HandleCommand(cmd, remote);
                }
                else
                {
                    _log.LogWarning("Heartbeat refusé par le serveur (jeton invalide ?).");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Serveur injoignable, nouvelle tentative.");
            }

            await Task.Delay(TimeSpan.FromSeconds(nextDelay), ct);
        }
    }

    private async Task<bool> TryEnrollAsync(CancellationToken ct)
    {
        var code = _cfg["Remina:EnrollmentCode"];
        if (string.IsNullOrWhiteSpace(code))
        {
            _log.LogError("Aucun code d'inscription (Remina:EnrollmentCode) configuré.");
            return false;
        }

        var hwId = _platform.GetHardwareId();
        var req = new EnrollRequest
        {
            EnrollmentCode = code,
            HardwareId = hwId,
            Inventory = _platform.CollectInventory(ReminaVersion.Semver),
        };

        var resp = await _client.EnrollAsync(req, ct);
        if (resp is null) return false;

        _state = new AgentState
        {
            MachineId = resp.MachineId,
            AgentToken = resp.AgentToken,
            MachineName = resp.MachineName,
            HardwareId = hwId,
        };
        _state.Save();
        _log.LogInformation("Poste inscrit : {Name} ({Id})", resp.MachineName, resp.MachineId);
        return true;
    }

    private async Task TrySelfUpdateAsync(CancellationToken ct)
    {
        try
        {
            var res = await _client.CheckUpdateAsync(Updater.CurrentRid, ReminaVersion.Semver, ct);
            if (res is null || !res.UpdateAvailable || res.DownloadUrl is null) return;

            _log.LogInformation("Mise à jour agent {Ver} disponible.", res.LatestVersion);
            using var http = ServerClient.DownloadClient();
            await Updater.DownloadAndStageAsync(http, res.DownloadUrl,
                AppContext.BaseDirectory, m => _log.LogInformation("{M}", m), ct);
            Updater.LaunchHelperAndExit(AppContext.BaseDirectory, "Remina.Agent",
                ServiceName(), m => _log.LogInformation("{M}", m));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Auto-update agent : échec de la vérification.");
        }
    }

    private string? ServiceName() =>
        _cfg["Remina:Update:ServiceName"]
        ?? (OperatingSystem.IsWindows() ? "ReminaAgent"
            : OperatingSystem.IsLinux() ? "remina-agent"
            : OperatingSystem.IsMacOS() ? "fr.nounourscorp.remina.agent" : null);

    private void HandleCommand(AgentCommand cmd, bool remoteSessionActive)
    {
        switch (cmd.Type)
        {
            case AgentCommandType.ShowMessage:
                _log.LogInformation("Message serveur : {Msg}", cmd.Payload);
                break;
            case AgentCommandType.Lock:
                _log.LogInformation("Ordre : verrouiller la session.");
                break;
            case AgentCommandType.Reboot:
                _log.LogInformation("Ordre : redémarrer.");
                break;
            case AgentCommandType.PrintSummaryTicket:
                _log.LogInformation("Ordre : imprimer le ticket récapitulatif.");
                break;
            case AgentCommandType.AutoLogin:
                if (remoteSessionActive)
                    _log.LogInformation("Auto-login ignoré : session RDP active.");
                else
                    _log.LogInformation("Ordre : connexion automatique de session.");
                break;
        }
        // v0 : les ordres sont tracés. Les actions système arriveront par OS,
        // chacune dans son module de plateforme, avec le cadre de consentement.
    }
}

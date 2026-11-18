using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
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
    private readonly AgentStatus _status;
    private readonly string _serverUrl;
    private readonly HashSet<Guid> _handled = new();
    private HubConnection? _hub;
    private AgentState _state;

    public AgentWorker(ILogger<AgentWorker> log, IConfiguration cfg, AgentStatus status)
    {
        _log = log;
        _cfg = cfg;
        _status = status;
        _platform = PlatformFactory.Create();
        _state = AgentState.Load() ?? new AgentState();
        // Priorité à l'URL mémorisée lors de l'inscription, sinon la config.
        _serverUrl = !string.IsNullOrWhiteSpace(_state.ServerUrl)
            ? _state.ServerUrl
            : (cfg["Remina:ServerUrl"] ?? "http://localhost:5080");
        _client = new ServerClient(_serverUrl);
        _status.ServerUrl = _serverUrl;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("Agent Remina {Version} — OS {Os}", ReminaVersion.Full, _platform.Os);

        if (_state.IsEnrolled)
        {
            _status.Enrolled = true;
            _status.MachineName = _state.MachineName;
            _status.NotifyChanged();
        }

        // Imprimantes : liste + choix mémorisé, et persistance du choix fait depuis l'UI.
        _status.Printers = _platform.GetPrinters();
        _status.PrinterName = !string.IsNullOrWhiteSpace(_state.PrinterName)
            ? _state.PrinterName : (_cfg["Remina:PrinterName"] ?? "");
        _status.OnPrinterChosen = p =>
        {
            _state.PrinterName = p;
            try { _state.Save(); } catch { }
        };

        // Inscription : on réessaie jusqu'à réussir (serveur momentanément injoignable, etc.).
        while (!_state.IsEnrolled && !ct.IsCancellationRequested)
        {
            if (await TryEnrollAsync(ct)) break;
            _log.LogWarning("Inscription impossible (code ou serveur). Nouvelle tentative dans 30 s.");
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }

        // Connexion temps réel : commandes poussées sans attendre le heartbeat.
        await ConnectHubAsync(ct);

        var idleThreshold = TimeSpan.FromMinutes(
            _cfg.GetValue("Remina:IdleMinutes", 5));
        int nextDelay = 30;

        // Suivi d'activité : boucle dédiée (échantillonnage court, envoi périodique).
        _status.ActivityEnabled = _cfg.GetValue("Remina:Activity:Enabled", true);
        if (_status.ActivityEnabled)
            _ = Task.Run(() => ActivityLoopAsync(idleThreshold, ct), ct);

        var updateEnabled = _cfg.GetValue("Remina:Update:Enabled", true);
        var updateEvery = TimeSpan.FromHours(_cfg.GetValue("Remina:Update:CheckHours", 6));
        var nextUpdateCheck = DateTimeOffset.UtcNow.AddMinutes(1); // première vérif après 1 min

        var inventoryEnabled = _cfg.GetValue("Remina:Inventory:Enabled", true);
        var nextInventory = DateTimeOffset.UtcNow; // inventaire dès le premier passage

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (updateEnabled && DateTimeOffset.UtcNow >= nextUpdateCheck)
                {
                    nextUpdateCheck = DateTimeOffset.UtcNow + updateEvery;
                    await TrySelfUpdateAsync(ct); // ne revient pas si une mise à jour s'applique
                }

                if (inventoryEnabled && _state.IsEnrolled && DateTimeOffset.UtcNow >= nextInventory)
                {
                    nextInventory = DateTimeOffset.UtcNow + TimeSpan.FromHours(24);
                    _ = Task.Run(() => ReportInventoryAsync(ct));
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

                var (result, resp) = await _client.HeartbeatAsync(_state.AgentToken, hb, ct);
                if (result == HeartbeatResult.Ok && resp is not null)
                {
                    nextDelay = resp.NextHeartbeatSeconds;
                    _status.LastHeartbeatOk = DateTimeOffset.UtcNow;
                    _status.RemoteSessionActive = remote;
                    _status.LastError = null;
                    _status.NotifyChanged();
                    foreach (var cmd in resp.Commands)
                        Dispatch(cmd);
                }
                else if (result == HeartbeatResult.Unauthorized)
                {
                    // Le serveur ne reconnaît plus ce poste (supprimé, base réinitialisée) :
                    // on efface l'identité locale et on se réinscrit.
                    _log.LogWarning("Poste non reconnu par le serveur — réinscription.");
                    _status.LastError = "Réinscription en cours";
                    _status.Enrolled = false;
                    _status.NotifyChanged();
                    await ReEnrollAsync(ct);
                }
                else
                {
                    _log.LogWarning("Serveur injoignable au heartbeat.");
                    _status.LastError = "Serveur injoignable";
                    _status.NotifyChanged();
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
            ServerUrl = _serverUrl, // mémorise le serveur effectivement utilisé
        };
        try
        {
            _state.Save();
        }
        catch (Exception ex)
        {
            // L'inscription a réussi côté serveur ; un échec d'écriture ne doit pas tout arrêter.
            // La session continue ; au prochain démarrage, le serveur réutilise le poste (même HardwareId).
            _log.LogWarning(ex, "Inscription OK mais impossible d'enregistrer l'état local.");
        }
        _status.Enrolled = true;
        _status.MachineName = resp.MachineName;
        _status.NotifyChanged();
        _log.LogInformation("Poste inscrit : {Name} ({Id})", resp.MachineName, resp.MachineId);
        return true;
    }

    private async Task TrySelfUpdateAsync(CancellationToken ct)
    {
        try
        {
            var (app, binary, service, relaunchExe) = Variant();
            var res = await _client.CheckUpdateAsync(app, Updater.CurrentRid, ReminaVersion.Semver, ct);
            if (res is null || !res.UpdateAvailable || res.DownloadUrl is null) return;

            _log.LogInformation("Mise à jour {App} {Ver} disponible.", app, res.LatestVersion);
            using var http = ServerClient.DownloadClient();
            await Updater.DownloadAndStageAsync(http, res.DownloadUrl,
                AppContext.BaseDirectory, m => _log.LogInformation("{M}", m), ct);
            Updater.LaunchHelperAndExit(AppContext.BaseDirectory, binary, service, relaunchExe,
                m => _log.LogInformation("{M}", m));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Auto-update : échec de la vérification.");
        }
    }

    /// <summary>
    /// Identité de la variante pour la mise à jour : clé d'app, binaire, service, mode de relance.
    /// L'agent graphique (Remina.Agent.Tray) se relance par l'exécutable ; l'agent service
    /// redémarre son service.
    /// </summary>
    private (string app, string binary, string? service, bool relaunchExe) Variant()
    {
        var exe = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "") ?? "";
        if (exe.Equals("Remina.Agent.Tray", StringComparison.OrdinalIgnoreCase))
            return ("agent-gui", "Remina.Agent.Tray", null, true);
        return ("agent", "Remina.Agent", ServiceName(), false);
    }

    private string? ServiceName() =>
        _cfg["Remina:Update:ServiceName"]
        ?? (OperatingSystem.IsWindows() ? "ReminaAgent"
            : OperatingSystem.IsLinux() ? "remina-agent"
            : OperatingSystem.IsMacOS() ? "fr.nounourscorp.remina.agent" : null);

    /// <summary>
    /// Échantillonne l'appli au premier plan et envoie les cumuls au serveur.
    /// Rien n'est mesuré pendant l'inactivité ni en mode privé.
    /// </summary>
    private async Task ActivityLoopAsync(TimeSpan idleThreshold, CancellationToken ct)
    {
        var sampleSeconds = Math.Max(5, _cfg.GetValue("Remina:Activity:SampleSeconds", 15));
        var uploadEvery = TimeSpan.FromMinutes(_cfg.GetValue("Remina:Activity:UploadMinutes", 2));
        var captureTitles = _cfg.GetValue("Remina:Activity:CaptureTitles", false);
        var sampler = new ActivitySampler(_platform, captureTitles);
        var nextUpload = DateTimeOffset.UtcNow + uploadEvery;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var active = !_status.PrivateMode && !_platform.IsUserIdle(idleThreshold);
                if (active) sampler.Sample(sampleSeconds);

                if (DateTimeOffset.UtcNow >= nextUpload && sampler.HasData && _state.IsEnrolled)
                {
                    nextUpload = DateTimeOffset.UtcNow + uploadEvery;
                    var report = new ActivityReport { MachineId = _state.MachineId, Entries = sampler.Drain() };
                    await _client.PostActivityAsync(_state.AgentToken, report, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Suivi d'activité : échec d'un cycle.");
            }

            await Task.Delay(TimeSpan.FromSeconds(sampleSeconds), ct);
        }
    }

    /// <summary>Efface l'identité et réessaie l'inscription (après un refus serveur).</summary>
    private async Task ReEnrollAsync(CancellationToken ct)
    {
        _state = new AgentState();
        if (_hub is not null) { try { await _hub.DisposeAsync(); } catch { } _hub = null; }

        while (!_state.IsEnrolled && !ct.IsCancellationRequested)
        {
            if (await TryEnrollAsync(ct))
            {
                await ConnectHubAsync(ct);
                return;
            }
            _log.LogWarning("Réinscription impossible (code épuisé ou serveur injoignable). Nouvel essai dans 30 s.");
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    private async Task ConnectHubAsync(CancellationToken ct)
    {
        try
        {
            var url = _serverUrl.TrimEnd('/') +
                $"/hubs/agent?machineId={_state.MachineId}&token={Uri.EscapeDataString(_state.AgentToken)}";
            _hub = new HubConnectionBuilder().WithUrl(url).WithAutomaticReconnect().Build();
            _hub.On<AgentCommand>("command", Dispatch);
            await _hub.StartAsync(ct);
            _log.LogInformation("Connecté au serveur en temps réel.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Temps réel indisponible, le heartbeat prend le relais.");
        }
    }

    /// <summary>Exécute une commande en évitant les doublons (push temps réel + heartbeat).</summary>
    private void Dispatch(AgentCommand cmd)
    {
        lock (_handled)
        {
            if (!_handled.Add(cmd.CommandId)) return;
        }
        HandleCommand(cmd, _platform.IsRemoteSessionActive());
    }

    private void HandleCommand(AgentCommand cmd, bool remoteSessionActive)
    {
        switch (cmd.Type)
        {
            case AgentCommandType.ShowMessage:
                _log.LogInformation("Message à afficher : {Msg}", cmd.Payload);
                _platform.ShowMessage(cmd.Payload ?? "");
                break;
            case AgentCommandType.Lock:
                _log.LogInformation("Verrouillage de la session.");
                _platform.Lock();
                break;
            case AgentCommandType.Reboot:
                _log.LogInformation("Redémarrage du poste.");
                _platform.Reboot();
                break;
            case AgentCommandType.PrintSummaryTicket:
                var ticket = cmd.Payload ?? "";
                if (_status.HasPrintHandler)
                {
                    _log.LogInformation("Ticket reçu : demande de confirmation à l'utilisateur.");
                    _status.RequestPrint(ticket); // l'UI affiche le dialogue + compte à rebours
                }
                else
                {
                    _log.LogInformation("Ticket reçu : impression directe (pas d'interface).");
                    Printing.Print(ticket, _status.PrinterName);
                }
                break;
            case AgentCommandType.AutoLogin:
                if (remoteSessionActive)
                {
                    _log.LogInformation("Auto-login ignoré : session RDP active.");
                    break;
                }
                var user = _cfg["Remina:AutoLogin:User"];
                var pass = _cfg["Remina:AutoLogin:Password"];
                var domain = _cfg["Remina:AutoLogin:Domain"];
                if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
                {
                    _log.LogWarning("Auto-login demandé mais non configuré (Remina:AutoLogin:User/Password).");
                    break;
                }
                var applied = _platform.ConfigureAutoLogin(user, pass, domain);
                _log.LogInformation(applied
                    ? "Connexion automatique configurée."
                    : "Échec de la configuration auto-login (droits administrateur requis ?).");
                break;
            case AgentCommandType.InstallPackage:
                _ = Task.Run(() => InstallPackageAsync(cmd.CommandId, cmd.Payload));
                break;
        }
    }

    /// <summary>Télécharge et installe un paquet en silencieux, puis remonte le résultat.</summary>
    private async Task InstallPackageAsync(Guid commandId, string? payloadJson)
    {
        var ct = CancellationToken.None;
        try
        {
            var p = JsonSerializer.Deserialize<InstallPayload>(payloadJson ?? "");
            if (p is null || p.PackageId == Guid.Empty) return;

            var dir = Path.Combine(Path.GetTempPath(), "remina-pkg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, p.FileName);

            _log.LogInformation("Déploiement : téléchargement de {Name}…", p.FileName);
            var client = new ServerClient(_serverUrl, TimeSpan.FromMinutes(30));
            var ok = await client.DownloadPackageAsync(_state.AgentToken, _state.MachineId, p.PackageId, file, ct);
            if (!ok)
            {
                _log.LogWarning("Déploiement : téléchargement échoué.");
                await Report(commandId, false, -1, "Téléchargement échoué");
                return;
            }

            _log.LogInformation("Déploiement : installation de {Name}…", p.FileName);
            var (success, code) = Installer.Install(file, p.SilentArgs);
            if (success) _log.LogInformation("Déploiement réussi : {Name}.", p.FileName);
            else _log.LogWarning("Déploiement échoué ({Name}), code {Code}.", p.FileName, code);
            await Report(commandId, success, code, success ? "Installé" : "Échec (droits admin/root ?)");

            try { Directory.Delete(dir, recursive: true); } catch { }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Déploiement : erreur.");
            await Report(commandId, false, -1, ex.Message);
        }

        async Task Report(Guid id, bool success, int code, string msg) =>
            await _client.PostCommandResultAsync(_state.AgentToken, new CommandResult
            { MachineId = _state.MachineId, CommandId = id, Success = success, ExitCode = code, Message = msg }, ct);
    }

    private async Task ReportInventoryAsync(CancellationToken ct)
    {
        try
        {
            var items = _platform.GetInstalledSoftware();
            if (items.Count == 0) return;
            await _client.PostInventoryAsync(_state.AgentToken,
                new InventoryReport { MachineId = _state.MachineId, Items = items }, ct);
            _log.LogInformation("Inventaire logiciel envoyé ({N} entrées).", items.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Inventaire logiciel : échec.");
        }
    }
}

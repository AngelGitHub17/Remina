using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Remina.Shared;

/// <summary>
/// Mécanisme d'auto-mise à jour commun au serveur et à l'agent.
///
/// Principe (sans réécrire un binaire en cours d'exécution) :
///  1. Télécharge l'archive de la nouvelle version dans « update/ » à côté du binaire.
///  2. Écrit un petit script d'aide (bash ou cmd) qui, une fois le processus courant
///     terminé, recopie « update/ » par-dessus l'installation (sauf la config), puis
///     redémarre le service.
///  3. Lance ce script détaché et quitte le processus : le service manager et le script
///     font le reste.
///
/// Les unités systemd/launchd/Windows sont configurées pour NE PAS redémarrer sur sortie
/// propre (c'est le script qui relance), et pour ne pas tuer le script d'aide.
/// </summary>
public static class Updater
{
    public static string CurrentRid =>
        RuntimeInformation.RuntimeIdentifier; // ex : "linux-x64", "win-x64", "osx-arm64"

    /// <summary>Télécharge et met en attente la nouvelle version. Retourne le dossier « update/ ».</summary>
    public static async Task<string> DownloadAndStageAsync(
        HttpClient http, string downloadUrl, string appDir, Action<string> log, CancellationToken ct)
    {
        var stage = Path.Combine(appDir, "update");
        var tmpZip = Path.Combine(Path.GetTempPath(), $"remina-update-{Guid.NewGuid():N}.zip");

        log($"Téléchargement de la mise à jour : {downloadUrl}");
        await using (var s = await http.GetStreamAsync(downloadUrl, ct))
        await using (var f = File.Create(tmpZip))
            await s.CopyToAsync(f, ct);

        if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        Directory.CreateDirectory(stage);
        ZipFile.ExtractToDirectory(tmpZip, stage, overwriteFiles: true);
        File.Delete(tmpZip);
        log($"Mise à jour extraite dans {stage}");
        return stage;
    }

    /// <summary>
    /// Écrit le script d'aide, le lance détaché, puis quitte le processus courant.
    /// Ne retourne jamais.
    /// </summary>
    public static void LaunchHelperAndExit(
        string appDir, string binaryName, string? serviceName, Action<string> log)
    {
        var pid = Environment.ProcessId;
        if (OperatingSystem.IsWindows())
        {
            var helper = Path.Combine(appDir, "apply-update.cmd");
            File.WriteAllText(helper, WindowsHelper(appDir, binaryName, serviceName, pid));
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"\" /b \"{helper}\"")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = appDir,
            });
        }
        else
        {
            var helper = Path.Combine(appDir, "apply-update.sh");
            File.WriteAllText(helper, UnixHelper(appDir, binaryName, serviceName, pid));
            // chmod +x
            Process.Start(new ProcessStartInfo("chmod", $"+x \"{helper}\"") { UseShellExecute = false })?.WaitForExit(2000);
            // setsid détache le script du cgroup/éventuelle session du service.
            Process.Start(new ProcessStartInfo("/bin/bash", $"-c \"setsid bash '{helper}' >/tmp/remina-update.log 2>&1 &\"")
            {
                UseShellExecute = false, WorkingDirectory = appDir,
            });
        }
        log("Script de mise à jour lancé, arrêt du processus pour laisser la bascule se faire.");
        Environment.Exit(0);
    }

    private static string UnixHelper(string appDir, string binary, string? service, int pid) =>
$@"#!/usr/bin/env bash
set -e
# Attendre la fin du processus courant (max 60 s).
for i in $(seq 1 60); do kill -0 {pid} 2>/dev/null || break; sleep 1; done
cd '{appDir}'
# On ne touche pas à la config déjà en place.
rm -f update/appsettings*.json
cp -rf update/. ./
rm -rf update
chmod +x './{binary}' 2>/dev/null || true
# Redémarrage best-effort selon le gestionnaire de services disponible.
if command -v systemctl >/dev/null 2>&1 && [ -n '{service}' ]; then
  systemctl restart '{service}' || true
elif command -v launchctl >/dev/null 2>&1 && [ -n '{service}' ]; then
  launchctl kickstart -k system/'{service}' || true
fi
";

    private static string WindowsHelper(string appDir, string binary, string? service, int pid) =>
$@"@echo off
:waitloop
tasklist /FI ""PID eq {pid}"" 2>NUL | find ""{pid}"" >NUL
if not errorlevel 1 (
  timeout /t 1 /nobreak >NUL
  goto waitloop
)
cd /d ""{appDir}""
del /Q ""update\appsettings*.json"" >NUL 2>&1
xcopy /E /I /Y ""update"" ""{appDir}"" >NUL
rmdir /S /Q ""update""
if not ""{service}""=="""" sc start ""{service}"" >NUL 2>&1
";
}

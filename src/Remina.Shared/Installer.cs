using System.Diagnostics;

namespace Remina.Shared;

/// <summary>
/// Installation silencieuse d'un paquet téléchargé, selon le type de fichier et l'OS.
/// Les installations système requièrent des droits administrateur / root
/// (agent service / LocalSystem, ou root sous Linux/macOS).
/// </summary>
public static class Installer
{
    /// <summary>Lance l'installation et attend la fin. Retourne (succès, code de sortie).</summary>
    public static (bool ok, int exitCode) Install(string filePath, string? silentArgs)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (OperatingSystem.IsWindows())
        {
            if (ext == ".msi")
                return Run("msiexec", $"/i \"{filePath}\" /qn /norestart {silentArgs}".Trim());
            // .exe : les arguments silencieux dépendent de l'installeur (/S, /silent, /qn…).
            return Run(filePath, silentArgs ?? "");
        }

        if (OperatingSystem.IsMacOS())
        {
            if (ext == ".pkg")
                return Run("installer", $"-pkg \"{filePath}\" -target /");
            return Run("/bin/bash", $"\"{filePath}\" {silentArgs}".Trim());
        }

        // Linux
        if (ext == ".deb")
        {
            var r = Run("dpkg", $"-i \"{filePath}\"");
            if (!r.ok) Run("apt-get", "-f install -y"); // résout les dépendances
            return r;
        }
        if (ext == ".sh" || ext == ".run")
        {
            Run("chmod", $"+x \"{filePath}\"");
            return Run("/bin/bash", $"\"{filePath}\" {silentArgs}".Trim());
        }
        return Run("/bin/bash", $"\"{filePath}\" {silentArgs}".Trim());
    }

    private static (bool ok, int code) Run(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            { UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p is null) return (false, -1);
            p.WaitForExit(10 * 60 * 1000); // 10 min max
            return (p.ExitCode == 0, p.ExitCode);
        }
        catch { return (false, -1); }
    }
}

using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Remina.Shared;

namespace Remina.Agent.Platform;

/// <summary>
/// Parties d'inventaire communes aux trois OS, via les API .NET portables.
/// Les classes par OS héritent et surchargent ce qui leur est spécifique.
/// </summary>
public abstract class PlatformBase : IPlatform
{
    public abstract OsPlatform Os { get; }

    public virtual MachineInventory CollectInventory(string agentVersion) => new()
    {
        Os = Os,
        OsVersion = RuntimeInformation.OSDescription,
        Hostname = Environment.MachineName,
        CpuName = GetCpuName(),
        CpuCores = Environment.ProcessorCount,
        PrimaryMacAddress = GetPrimaryMac(),
        AgentVersion = agentVersion,
    };

    public virtual string? GetCurrentUser() => Environment.UserName;

    // Par défaut, aucune session distante détectée : surchargé sur Windows.
    public virtual bool IsRemoteSessionActive() => false;

    // Inactivité : surchargée par OS (API système). Faux par défaut (sans faux positifs).
    public virtual bool IsUserIdle(TimeSpan threshold) => false;

    public abstract string GetHardwareId();

    // --- Actions de commande (implémentations par OS, best-effort) ---

    public virtual void Reboot()
    {
        if (OperatingSystem.IsWindows()) Run("shutdown", "/r", "/t", "0");
        else if (OperatingSystem.IsMacOS()) Run("shutdown", "-r", "now");
        else Run("systemctl", "reboot");
    }

    public virtual void Lock()
    {
        if (OperatingSystem.IsWindows())
            Run("rundll32.exe", "user32.dll,LockWorkStation");
        else if (OperatingSystem.IsMacOS())
            Run("/System/Library/CoreServices/Menu Extras/User.menu/Contents/Resources/CGSession", "-suspend");
        else
            Run("loginctl", "lock-sessions");
    }

    public virtual void ShowMessage(string text)
    {
        if (OperatingSystem.IsWindows())
            Run("msg", "*", text); // msg.exe : présent sur Pro/Enterprise
        else if (OperatingSystem.IsMacOS())
            Run("osascript", "-e", $"display notification \"{text.Replace("\"", "'")}\" with title \"Remina\"");
        else
            Run("notify-send", "Remina", text);
    }

    // Premier plan : par défaut inconnu (surchargé sur Windows). Linux/Wayland et macOS
    // nécessiteront des autorisations/extensions dédiées, ajoutées plus tard.
    public virtual (string App, string? Title)? GetForegroundApp() => null;

    /// <summary>Lance un processus sans fenêtre, sans attendre, en avalant les erreurs.</summary>
    protected static void Run(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
        }
        catch { /* best-effort : l'action peut échouer selon l'OS/la session */ }
    }

    protected virtual string? GetCpuName() => null;

    protected static string? GetPrimaryMac()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n => n.GetPhysicalAddress().ToString())
                .FirstOrDefault(s => !string.IsNullOrEmpty(s));
        }
        catch { return null; }
    }

    /// <summary>Repli commun : GUID persisté localement si l'OS n'expose pas d'ID stable.</summary>
    protected static string StableGuidFallback()
    {
        var path = Path.Combine(ReminaPaths.DataDir, "hardware-id");
        if (File.Exists(path)) return File.ReadAllText(path).Trim();
        var id = Guid.NewGuid().ToString("N");
        File.WriteAllText(path, id);
        return id;
    }
}

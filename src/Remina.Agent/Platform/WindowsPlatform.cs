using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Remina.Shared;

namespace Remina.Agent.Platform;

public sealed class WindowsPlatform : PlatformBase
{
    public override OsPlatform Os => OsPlatform.Windows;

    public override string GetHardwareId()
    {
        // MachineGuid : stable, propre à l'installation Windows.
        var id = RunReg(@"HKLM\SOFTWARE\Microsoft\Cryptography", "MachineGuid");
        return string.IsNullOrWhiteSpace(id) ? StableGuidFallback() : id.Trim();
    }

    /// <summary>
    /// SM_REMOTESESSION indique si la session courante est servie via les
    /// Services Bureau à distance (RDP). Tant que c'est vrai, l'agent laisse
    /// la session distante tranquille et ne relance pas l'auto-login.
    /// </summary>
    public override bool IsRemoteSessionActive()
    {
        if (!OperatingSystem.IsWindows()) return false;
        return GetSystemMetrics(SM_REMOTESESSION) != 0;
    }

    public override bool IsUserIdle(TimeSpan threshold)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return false;
        var idleMs = (uint)Environment.TickCount - info.dwTime;
        return idleMs >= threshold.TotalMilliseconds;
    }

    protected override string? GetCpuName()
    {
        var v = RunReg(@"HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString");
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    private static string? RunReg(string key, string value)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var psi = new ProcessStartInfo("reg", $"query \"{key}\" /v {value}")
            {
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            // Dernière colonne de la ligne contenant le nom de la valeur.
            var line = output.Split('\n').FirstOrDefault(l => l.Contains(value));
            var parts = line?.Split("    ", StringSplitOptions.RemoveEmptyEntries);
            return parts is { Length: >= 3 } ? parts[^1] : null;
        }
        catch { return null; }
    }

    private const int SM_REMOTESESSION = 0x1000;

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}

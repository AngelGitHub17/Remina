using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Remina.Shared;

namespace Remina.Agent.Platform;

public sealed class WindowsPlatform : PlatformBase
{
    public override OsPlatform Os => OsPlatform.Windows;

    protected override string GetOsHardwareBase()
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

    public override IReadOnlyList<InventorySoftware> GetInstalledSoftware()
    {
        if (!OperatingSystem.IsWindows()) return base.GetInstalledSoftware();
        var list = new List<InventorySoftware>();
        try
        {
            var psi = new ProcessStartInfo("powershell")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(
                "Get-ItemProperty 'HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*','HKLM:\\Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*' " +
                "| Where-Object { $_.DisplayName } | ForEach-Object { \"$($_.DisplayName)`t$($_.DisplayVersion)\" }");
            using var p = Process.Start(psi);
            if (p is null) return list;
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            var seen = new HashSet<string>();
            foreach (var line in o.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t');
                var name = parts[0].Trim();
                if (name.Length == 0 || !seen.Add(name)) continue;
                list.Add(new InventorySoftware { Name = name, Version = parts.Length > 1 ? parts[1].Trim() : null });
            }
        }
        catch { }
        return list;
    }

    public override (string App, string? Title)? GetForegroundApp()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;

            string app;
            try { using var p = Process.GetProcessById((int)pid); app = p.ProcessName; }
            catch { return null; }

            var sb = new System.Text.StringBuilder(512);
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();
            return (app, string.IsNullOrWhiteSpace(title) ? null : title);
        }
        catch { return null; }
    }

    public override IReadOnlyList<string> GetPrinters()
    {
        if (!OperatingSystem.IsWindows()) return base.GetPrinters();
        try
        {
            var psi = new ProcessStartInfo("powershell")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add("Get-Printer | Select-Object -ExpandProperty Name");
            using var p = Process.Start(psi);
            if (p is null) return Array.Empty<string>();
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return o.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    // Connexion automatique sécurisée (secret LSA). Requiert des droits administrateur.
    public override bool ConfigureAutoLogin(string user, string password, string? domain)
        => OperatingSystem.IsWindows() && WindowsAutoLogin.Apply(user, password, domain);

    public override bool DisableAutoLogin()
        => OperatingSystem.IsWindows() && WindowsAutoLogin.Disable();

    private const int SM_REMOTESESSION = 0x1000;

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
}

using System.Diagnostics;
using Remina.Shared;

namespace Remina.Agent.Platform;

public sealed class MacPlatform : PlatformBase
{
    public override OsPlatform Os => OsPlatform.MacOs;

    public override string GetHardwareId()
    {
        // IOPlatformUUID : identifiant matériel stable propre au Mac.
        var uuid = RunShell("ioreg", "-rd1 -c IOPlatformExpertDevice");
        if (uuid is not null)
        {
            var line = uuid.Split('\n').FirstOrDefault(l => l.Contains("IOPlatformUUID"));
            var val = line?.Split('=').ElementAtOrDefault(1)?.Trim().Trim('"');
            if (!string.IsNullOrEmpty(val)) return val;
        }
        return StableGuidFallback();
    }

    protected override string? GetCpuName() =>
        RunShell("sysctl", "-n machdep.cpu.brand_string")?.Trim();

    private static string? RunShell(string file, string args)
    {
        if (!OperatingSystem.IsMacOS()) return null;
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            return output;
        }
        catch { return null; }
    }

    // Note : le suivi d'activité détaillé réclamera l'autorisation d'accessibilité macOS.

    public override IReadOnlyList<InventorySoftware> GetInstalledSoftware()
    {
        // Applications du dossier /Applications (versions non triviales à extraire ici).
        var list = new List<InventorySoftware>();
        try
        {
            foreach (var dir in Directory.GetDirectories("/Applications", "*.app"))
                list.Add(new InventorySoftware { Name = Path.GetFileNameWithoutExtension(dir) });
        }
        catch { }
        return list;
    }
}

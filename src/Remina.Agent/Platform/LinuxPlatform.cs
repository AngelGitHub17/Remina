using Remina.Shared;

namespace Remina.Agent.Platform;

public sealed class LinuxPlatform : PlatformBase
{
    public override OsPlatform Os => OsPlatform.Linux;

    protected override string GetOsHardwareBase()
    {
        // /etc/machine-id : identifiant stable fourni par systemd/dbus.
        foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
        {
            try
            {
                if (File.Exists(path))
                {
                    var id = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrEmpty(id)) return id;
                }
            }
            catch { /* on tente le suivant */ }
        }
        return StableGuidFallback();
    }

    protected override string? GetCpuName()
    {
        try
        {
            var line = File.ReadLines("/proc/cpuinfo")
                .FirstOrDefault(l => l.StartsWith("model name"));
            return line?.Split(':', 2).ElementAtOrDefault(1)?.Trim();
        }
        catch { return null; }
    }

    // Détection RDP : non pertinente sur Linux (pas de RDP natif).
    // L'inactivité sous Wayland nécessitera une extension de bureau : faux pour la v0.

    public override IReadOnlyList<InventorySoftware> GetInstalledSoftware()
    {
        // Debian/Ubuntu via dpkg, sinon RPM.
        var o = RunCapture("dpkg-query", "-W -f=${Package}\\t${Version}\\n");
        if (string.IsNullOrWhiteSpace(o))
            o = RunCapture("rpm", "-qa --qf %{NAME}\\t%{VERSION}\\n");
        return Parse(o);
    }

    private static List<InventorySoftware> Parse(string output)
    {
        var list = new List<InventorySoftware>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 1 && parts[0].Trim().Length > 0)
                list.Add(new InventorySoftware { Name = parts[0].Trim(), Version = parts.Length > 1 ? parts[1].Trim() : null });
        }
        return list;
    }
}

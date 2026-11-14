using Remina.Shared;

namespace Remina.Agent.Platform;

public sealed class LinuxPlatform : PlatformBase
{
    public override OsPlatform Os => OsPlatform.Linux;

    public override string GetHardwareId()
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
}

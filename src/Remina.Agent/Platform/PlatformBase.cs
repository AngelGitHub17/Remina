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

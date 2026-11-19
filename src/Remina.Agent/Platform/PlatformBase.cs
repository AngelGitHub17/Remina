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

    /// <summary>
    /// Identifiant matériel envoyé à l'inscription : base OS (MachineGuid, /etc/machine-id,
    /// IOPlatformUUID) + plus petite adresse MAC physique + nom d'hôte, hachés. Deux machines
    /// clonées (même MachineGuid) obtiennent des identifiants distincts.
    /// Il n'est calculé qu'à l'inscription puis mémorisé dans l'état local : au démarrage, la
    /// correspondance poste/identité est vérifiée par <see cref="GetFingerprint"/>, tolérante
    /// à l'ordre et à l'état des cartes réseau.
    /// </summary>
    public string GetHardwareId() =>
        HashHex($"{GetOsHardwareBase()}|{GetPhysicalMacs().FirstOrDefault() ?? ""}|{Environment.MachineName}");

    public HardwareFingerprint GetFingerprint() =>
        new(HashHex(GetOsHardwareBase()), GetPhysicalMacs(), Environment.MachineName);

    /// <summary>
    /// Vérifie un identifiant produit par les agents 0.1.12–0.1.14, qui hachaient la MAC de la
    /// première carte « Up » (variable selon l'ordre de démarrage du réseau) : on essaie
    /// toutes les MAC présentes, ainsi que « aucune MAC ».
    /// </summary>
    public bool MatchesLegacyHardwareId(string hardwareId)
    {
        var osBase = GetOsHardwareBase();
        var candidates = new List<string> { "" };
        try
        {
            candidates.AddRange(NetworkInterface.GetAllNetworkInterfaces()
                .Select(n => n.GetPhysicalAddress().ToString())
                .Where(m => !string.IsNullOrEmpty(m)));
        }
        catch { }
        return candidates.Distinct().Any(mac => string.Equals(
            HashHex($"{osBase}|{mac}|{Environment.MachineName}"), hardwareId,
            StringComparison.OrdinalIgnoreCase));
    }

    private static string HashHex(string raw) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw)));

    // Interfaces virtuelles/VPN/conteneurs : apparaissent et disparaissent, à ignorer.
    private static readonly string[] VirtualMarkers =
    {
        "virtual", "hyper-v", "vmware", "virtualbox", "vbox", "vpn", "wireguard", "tailscale",
        "zerotier", "docker", "bluetooth", "wsl", "npcap", "loopback", "tap-", "tap0",
        "openvpn", "fortinet", "anyconnect", "vethernet", "pseudo",
    };
    private static readonly string[] VirtualNamePrefixes =
    {
        "veth", "br-", "virbr", "vmnet", "tun", "tap", "wg", "zt", "docker", "lxc", "lxd",
        "utun", "awdl", "llw", "bridge", "anpi", "gif", "stf", "ap1",
    };

    /// <summary>
    /// Adresses MAC des cartes physiques (Ethernet/Wi-Fi), <b>quel que soit leur état</b>
    /// (une carte pas encore montée au démarrage reste prise en compte), triées.
    /// Exclut les adresses aléatoires (bit « administrée localement ») et les cartes virtuelles.
    /// </summary>
    protected static IReadOnlyList<string> GetPhysicalMacs()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                    or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet
                    or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx
                    or NetworkInterfaceType.Ethernet3Megabit)
                .Where(n =>
                {
                    var name = n.Name.ToLowerInvariant();
                    var text = name + " " + (n.Description ?? "").ToLowerInvariant();
                    return !VirtualMarkers.Any(text.Contains)
                        && !VirtualNamePrefixes.Any(name.StartsWith);
                })
                .Select(n => n.GetPhysicalAddress().ToString().ToUpperInvariant())
                .Where(IsGlobalMac)
                .Distinct()
                .OrderBy(m => m, StringComparer.Ordinal)
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    private static bool IsGlobalMac(string mac) =>
        mac.Length == 12 && mac != "000000000000"
        && byte.TryParse(mac.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var b)
        && (b & 0x02) == 0;

    /// <summary>Identifiant propre à l'OS (surchargé par plateforme).</summary>
    protected abstract string GetOsHardwareBase();

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

    // Imprimantes via CUPS par défaut (Linux/macOS) ; Windows surcharge.
    public virtual IReadOnlyList<string> GetPrinters()
    {
        try
        {
            var psi = new ProcessStartInfo("lpstat", "-e")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p is null) return Array.Empty<string>();
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            return o.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    // Auto-login : propre à Windows ; ailleurs, se configure via le gestionnaire d'affichage.
    public virtual bool ConfigureAutoLogin(string user, string password, string? domain) => false;
    public virtual bool DisableAutoLogin() => false;

    // Inventaire logiciel : surchargé par OS.
    public virtual IReadOnlyList<InventorySoftware> GetInstalledSoftware() => Array.Empty<InventorySoftware>();

    /// <summary>Lance un processus et renvoie sa sortie standard (vide en cas d'échec).</summary>
    protected static string RunCapture(string file, string args, int timeoutMs = 8000)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p is null) return "";
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(timeoutMs);
            return o;
        }
        catch { return ""; }
    }

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

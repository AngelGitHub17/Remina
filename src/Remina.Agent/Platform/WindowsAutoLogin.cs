using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Remina.Agent.Platform;

/// <summary>
/// Connexion automatique Windows sécurisée : le mot de passe est stocké comme
/// <b>secret LSA chiffré</b> (clé « DefaultPassword »), jamais en clair dans le registre
/// — même mécanisme que l'outil Sysinternals Autologon. Les clés Winlogon ne contiennent
/// que l'utilisateur et le domaine. Requiert des droits administrateur.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsAutoLogin
{
    private const string WinlogonKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";

    public static bool Apply(string user, string password, string? domain)
    {
        try
        {
            if (!StoreSecret("DefaultPassword", password)) return false;
            RegAdd("AutoAdminLogon", "1");
            RegAdd("DefaultUserName", user);
            RegAdd("DefaultDomainName", domain ?? Environment.MachineName);
            // Plus de mot de passe en clair : Winlogon lira le secret LSA.
            RegDelete("DefaultPassword");
            return true;
        }
        catch { return false; }
    }

    public static bool Disable()
    {
        try
        {
            RegAdd("AutoAdminLogon", "0");
            StoreSecret("DefaultPassword", null); // efface le secret
            RegDelete("DefaultPassword");
            return true;
        }
        catch { return false; }
    }

    // --- Secret LSA ---

    private static bool StoreSecret(string key, string? value)
    {
        var system = default(LSA_UNICODE_STRING);
        var attrs = new LSA_OBJECT_ATTRIBUTES();
        const uint POLICY_CREATE_SECRET = 0x00000020;
        const uint POLICY_GET_PRIVATE_INFORMATION = 0x00000004;

        if (LsaOpenPolicy(ref system, ref attrs,
                POLICY_CREATE_SECRET | POLICY_GET_PRIVATE_INFORMATION, out var policy) != 0)
            return false;

        try
        {
            var keyName = ToLsaString(key);
            if (value is null)
            {
                var empty = default(LSA_UNICODE_STRING);
                return LsaStorePrivateData(policy, ref keyName, ref empty) == 0;
            }

            var data = ToLsaString(value);
            try { return LsaStorePrivateData(policy, ref keyName, ref data) == 0; }
            finally { if (data.Buffer != IntPtr.Zero) Marshal.FreeHGlobal(data.Buffer); }
        }
        finally { LsaClose(policy); }
    }

    private static LSA_UNICODE_STRING ToLsaString(string s) => new()
    {
        Buffer = Marshal.StringToHGlobalUni(s),
        Length = (ushort)(s.Length * 2),
        MaximumLength = (ushort)((s.Length + 1) * 2),
    };

    // --- Registre (via reg.exe, simple et fiable depuis un processus élevé) ---

    private static void RegAdd(string name, string data) =>
        Run("reg", "add", WinlogonKey, "/v", name, "/t", "REG_SZ", "/d", data, "/f");

    private static void RegDelete(string name) =>
        Run("reg", "delete", WinlogonKey, "/v", name, "/f");

    private static void Run(string file, params string[] args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(file)
            { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(4000);
        }
        catch { }
    }

    // --- P/Invoke LSA ---

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_UNICODE_STRING { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_OBJECT_ATTRIBUTES
    {
        public int Length; public IntPtr RootDirectory; public IntPtr ObjectName;
        public uint Attributes; public IntPtr SecurityDescriptor; public IntPtr SecurityQualityOfService;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint LsaOpenPolicy(ref LSA_UNICODE_STRING SystemName,
        ref LSA_OBJECT_ATTRIBUTES ObjectAttributes, uint DesiredAccess, out IntPtr PolicyHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint LsaStorePrivateData(IntPtr PolicyHandle,
        ref LSA_UNICODE_STRING KeyName, ref LSA_UNICODE_STRING PrivateData);

    [DllImport("advapi32.dll")]
    private static extern uint LsaClose(IntPtr ObjectHandle);
}

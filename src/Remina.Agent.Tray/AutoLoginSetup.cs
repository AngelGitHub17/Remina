using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Remina.Agent.Platform;

namespace Remina.Agent.Tray;

/// <summary>
/// Configuration locale de la connexion automatique : les identifiants ne quittent jamais
/// le poste. Ils sont chiffrés (DPAPI, portée utilisateur) dans un fichier temporaire, puis
/// une instance élevée (UAC) les lit et écrit le secret LSA + les clés Winlogon.
/// </summary>
public static class AutoLoginSetup
{
    private record Creds(string User, string Password, string? Domain);

    /// <summary>Appelé depuis l'UI (non élevée) : relance l'exe en administrateur.</summary>
    public static void RequestApply(string user, string password, string? domain)
    {
        var json = JsonSerializer.Serialize(new Creds(user, password, domain));
        var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
        var file = Path.Combine(Path.GetTempPath(), $"remina-al-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(file, enc);
        RelaunchElevated($"--setup-autologin \"{file}\"");
    }

    public static void RequestDisable() => RelaunchElevated("--disable-autologin");

    private static void RelaunchElevated(string args)
    {
        try
        {
            var exe = Environment.ProcessPath!;
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,   // requis pour le verbe runas (UAC)
                Verb = "runas",
                Arguments = args,
            });
        }
        catch { /* l'utilisateur a refusé l'UAC */ }
    }

    /// <summary>
    /// Appelé dans l'instance élevée (via les arguments). Applique puis quitte.
    /// Retourne vrai si l'argument correspondait à une action auto-login.
    /// </summary>
    public static bool HandleElevatedArgs(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return false;

        if (args.Contains("--disable-autologin"))
        {
            WindowsAutoLogin.Disable();
            return true;
        }

        var idx = Array.IndexOf(args, "--setup-autologin");
        if (idx >= 0 && idx + 1 < args.Length)
        {
            var file = args[idx + 1];
            try
            {
                var enc = File.ReadAllBytes(file);
                var json = Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser));
                var c = JsonSerializer.Deserialize<Creds>(json);
                if (c is not null) WindowsAutoLogin.Apply(c.User, c.Password, c.Domain);
            }
            catch { }
            finally { try { File.Delete(file); } catch { } }
            return true;
        }
        return false;
    }
}

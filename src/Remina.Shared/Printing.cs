using System.Diagnostics;

namespace Remina.Shared;

/// <summary>
/// Impression d'un texte sur l'imprimante par défaut du poste.
/// Windows : PowerShell Out-Printer. Linux/macOS : CUPS (lp).
/// </summary>
public static class Printing
{
    public static bool Print(string text)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"remina-ticket-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tmp, text);
            if (OperatingSystem.IsWindows())
                return Run("powershell", "-NoProfile", "-Command",
                    $"Get-Content -LiteralPath '{tmp}' -Raw | Out-Printer");
            return Run("lp", tmp); // CUPS (Linux/macOS)
        }
        catch { return false; }
        finally
        {
            // Laisse un court délai à l'impression avant de nettoyer.
            _ = Task.Run(async () => { await Task.Delay(TimeSpan.FromMinutes(1)); try { File.Delete(tmp); } catch { } });
        }
    }

    private static bool Run(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            return p is not null;
        }
        catch { return false; }
    }
}

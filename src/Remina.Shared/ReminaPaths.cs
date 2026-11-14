namespace Remina.Shared;

/// <summary>
/// Emplacements inscriptibles pour les données persistantes (état, identifiant matériel).
/// Jamais dans le dossier d'installation : sous Windows il est en lecture seule pour
/// l'utilisateur, et il est écrasé aux mises à jour.
/// </summary>
public static class ReminaPaths
{
    /// <summary>Dossier de données, créé si besoin. Repli sur le dossier applicatif en cas d'échec.</summary>
    public static string DataDir
    {
        get
        {
            string dir;
            if (OperatingSystem.IsWindows())
                dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Remina");
            else if (OperatingSystem.IsMacOS())
                dir = "/Library/Application Support/Remina";
            else
                dir = "/var/lib/remina";

            try
            {
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch
            {
                // Dernier recours : à côté de l'exécutable (mieux que planter).
                return AppContext.BaseDirectory;
            }
        }
    }
}

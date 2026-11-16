using Microsoft.EntityFrameworkCore;

namespace Remina.Server.Data;

/// <summary>
/// Synchronise le schéma SQLite de façon idempotente : crée les tables/colonnes ajoutées
/// après la création initiale de la base. Nécessaire car EnsureCreated() ne fait rien sur
/// une base existante (pas de migrations). Sûr à exécuter à chaque démarrage.
/// </summary>
public static class SchemaGuard
{
    public static void Ensure(ReminaDbContext db)
    {
        var sql = new[]
        {
            @"CREATE TABLE IF NOT EXISTS ""Bureaus"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Bureaus"" PRIMARY KEY,
                ""Name"" TEXT NOT NULL,
                ""Description"" TEXT NULL,
                ""Location"" TEXT NULL,
                ""CreatedAt"" TEXT NOT NULL);",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Bureaus_Name"" ON ""Bureaus"" (""Name"");",

            @"CREATE TABLE IF NOT EXISTS ""Activity"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Activity"" PRIMARY KEY,
                ""MachineId"" TEXT NOT NULL,
                ""Day"" TEXT NOT NULL,
                ""App"" TEXT NOT NULL,
                ""Title"" TEXT NOT NULL,
                ""Seconds"" INTEGER NOT NULL);",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Activity_MachineId_Day_App_Title""
                ON ""Activity"" (""MachineId"",""Day"",""App"",""Title"");",

            @"CREATE TABLE IF NOT EXISTS ""TimeEntries"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_TimeEntries"" PRIMARY KEY,
                ""Project"" TEXT NOT NULL,
                ""Start"" TEXT NOT NULL,
                ""End"" TEXT NOT NULL,
                ""Note"" TEXT NULL,
                ""Source"" TEXT NOT NULL,
                ""CreatedAt"" TEXT NOT NULL);",
        };

        foreach (var s in sql)
            db.Database.ExecuteSqlRaw(s);

        // Colonne ajoutée à une table existante : ALTER échoue si elle existe déjà → on ignore.
        TryExec(db, @"ALTER TABLE ""Machines"" ADD COLUMN ""BureauId"" TEXT NULL;");
    }

    private static void TryExec(ReminaDbContext db, string sql)
    {
        try { db.Database.ExecuteSqlRaw(sql); } catch { /* déjà présent */ }
    }
}

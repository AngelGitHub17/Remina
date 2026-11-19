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

            @"CREATE TABLE IF NOT EXISTS ""Packages"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Packages"" PRIMARY KEY,
                ""Name"" TEXT NOT NULL,
                ""FileName"" TEXT NOT NULL,
                ""Os"" INTEGER NOT NULL,
                ""SilentArgs"" TEXT NULL,
                ""SizeBytes"" INTEGER NOT NULL,
                ""UploadedAt"" TEXT NOT NULL);",

            @"CREATE TABLE IF NOT EXISTS ""Inventory"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Inventory"" PRIMARY KEY,
                ""MachineId"" TEXT NOT NULL,
                ""Name"" TEXT NOT NULL,
                ""Version"" TEXT NULL);",
            @"CREATE INDEX IF NOT EXISTS ""IX_Inventory_MachineId"" ON ""Inventory"" (""MachineId"");",

            // v0.2.0 — comptes, sessions, réglages.
            @"CREATE TABLE IF NOT EXISTS ""AdminUsers"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_AdminUsers"" PRIMARY KEY,
                ""Username"" TEXT NOT NULL,
                ""DisplayName"" TEXT NOT NULL,
                ""PasswordHash"" TEXT NULL,
                ""Role"" INTEGER NOT NULL,
                ""IsDisabled"" INTEGER NOT NULL,
                ""FailedLogins"" INTEGER NOT NULL,
                ""LockoutUntil"" TEXT NULL,
                ""ExternalProvider"" TEXT NULL,
                ""ExternalId"" TEXT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""LastLoginAt"" TEXT NULL);",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AdminUsers_Username"" ON ""AdminUsers"" (""Username"");",

            @"CREATE TABLE IF NOT EXISTS ""AdminSessions"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_AdminSessions"" PRIMARY KEY,
                ""TokenHash"" TEXT NOT NULL,
                ""UserId"" TEXT NOT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""ExpiresAt"" TEXT NOT NULL,
                ""LastSeenAt"" TEXT NOT NULL,
                ""Ip"" TEXT NULL,
                ""UserAgent"" TEXT NULL,
                ""RevokedAt"" TEXT NULL);",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AdminSessions_TokenHash"" ON ""AdminSessions"" (""TokenHash"");",
            @"CREATE INDEX IF NOT EXISTS ""IX_AdminSessions_UserId"" ON ""AdminSessions"" (""UserId"");",

            @"CREATE TABLE IF NOT EXISTS ""ServerSettings"" (
                ""Key"" TEXT NOT NULL CONSTRAINT ""PK_ServerSettings"" PRIMARY KEY,
                ""Value"" TEXT NOT NULL);",

            // v0.2.1 — journal d'audit, jetons d'API.
            @"CREATE TABLE IF NOT EXISTS ""AuditLog"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_AuditLog"" PRIMARY KEY,
                ""At"" TEXT NOT NULL,
                ""AtUnixMs"" INTEGER NOT NULL,
                ""UserId"" TEXT NULL,
                ""Username"" TEXT NOT NULL,
                ""Action"" TEXT NOT NULL,
                ""TargetType"" TEXT NULL,
                ""TargetId"" TEXT NULL,
                ""Details"" TEXT NULL,
                ""Ip"" TEXT NULL,
                ""Success"" INTEGER NOT NULL,
                ""StatusCode"" INTEGER NULL);",
            @"CREATE INDEX IF NOT EXISTS ""IX_AuditLog_AtUnixMs"" ON ""AuditLog"" (""AtUnixMs"");",
            @"CREATE INDEX IF NOT EXISTS ""IX_AuditLog_UserId"" ON ""AuditLog"" (""UserId"");",

            @"CREATE TABLE IF NOT EXISTS ""ApiTokens"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_ApiTokens"" PRIMARY KEY,
                ""UserId"" TEXT NOT NULL,
                ""Name"" TEXT NOT NULL,
                ""TokenHash"" TEXT NOT NULL,
                ""Prefix"" TEXT NOT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""LastUsedAt"" TEXT NULL,
                ""ExpiresAt"" TEXT NULL,
                ""RevokedAt"" TEXT NULL);",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ApiTokens_TokenHash"" ON ""ApiTokens"" (""TokenHash"");",
            @"CREATE INDEX IF NOT EXISTS ""IX_ApiTokens_UserId"" ON ""ApiTokens"" (""UserId"");",
        };

        foreach (var s in sql)
            db.Database.ExecuteSqlRaw(s);

        // Colonnes ajoutées à des tables existantes : ALTER échoue si déjà présentes → on ignore.
        TryExec(db, @"ALTER TABLE ""Machines"" ADD COLUMN ""BureauId"" TEXT NULL;");
        TryExec(db, @"ALTER TABLE ""AgentCommands"" ADD COLUMN ""Success"" INTEGER NULL;");
        TryExec(db, @"ALTER TABLE ""AgentCommands"" ADD COLUMN ""ExitCode"" INTEGER NULL;");
        TryExec(db, @"ALTER TABLE ""AgentCommands"" ADD COLUMN ""ResultMessage"" TEXT NULL;");
        TryExec(db, @"ALTER TABLE ""AgentCommands"" ADD COLUMN ""CompletedAt"" TEXT NULL;");
        // v0.2.2 — sessions ouvertes via NounoursAccount.
        TryExec(db, @"ALTER TABLE ""AdminSessions"" ADD COLUMN ""ExternalLink"" TEXT NULL;");
        TryExec(db, @"ALTER TABLE ""AdminSessions"" ADD COLUMN ""ExternalCheckedAt"" TEXT NULL;");
    }

    private static void TryExec(ReminaDbContext db, string sql)
    {
        try { db.Database.ExecuteSqlRaw(sql); } catch { /* déjà présent */ }
    }
}

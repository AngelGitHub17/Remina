# Remina — contexte projet

Logiciel de gestion de parc client/serveur multiplateforme (façon GLPI), .NET 8.
**Lire `docs/ROADMAP.md`** pour l'état complet, les fonctionnalités faites et la feuille de
route v0.2.0.

## Structure
- `src/Remina.Shared` — contrats, versionnage, auto-updater, impression, installeur.
- `src/Remina.Server` — API ASP.NET Core + EF Core (SQLite), SignalR, console web (`wwwroot/`).
- `src/Remina.Agent` — agent service (Windows service / systemd / launchd).
- `src/Remina.Agent.Tray` — agent graphique Avalonia (réutilise le cœur de Remina.Agent).
- `packaging/` installateurs, `ct/` scripts Proxmox, `.github/workflows/release.yml` CI.

## Commandes
```bash
dotnet build -c Release                  # tout
dotnet run --project src/Remina.Server   # serveur (http://localhost:5080, clé admin dans appsettings)
dotnet run --project src/Remina.Agent    # agent service (config dans appsettings/env Remina__*)
dotnet run --project src/Remina.Agent.Tray  # agent graphique
```

## Conventions
- Version dans `src/Remina.Shared/ReminaVersion.cs`. **Chaque itération = une nouvelle version**
  (bump `Semver`). Série 0.x.x = nom de code « Remina ».
- Publier = tag `vX.Y.Z` → la CI construit la Release ; l'auto-update s'appuie dessus.
- SQLite : pas de migrations EF ; `SchemaGuard` crée au démarrage les tables/colonnes
  ajoutées. **À chaque nouvelle table/colonne, mettre à jour `SchemaGuard`.**
- Attention SQLite : pas de `OrderBy`/`GroupBy` sur `DateTimeOffset`/enum traduisibles →
  matérialiser (`ToListAsync`) puis trier/grouper en mémoire.
- Console web = `src/Remina.Server/wwwroot/index.html` (vanilla JS, une page).

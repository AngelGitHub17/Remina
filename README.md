# Remina

Logiciel de gestion de parc de postes, client/serveur, multiplateforme (façon GLPI),
pensé pour NounoursCorp.

**Version : `0.0.1-20261114-Remina`**
Convention : toutes les versions `0.x.x` portent le nom de code **Remina**. Le nom ne
change qu'au passage à `1.0.0`, puis `2.0.0`, etc.

## Ce que fait la v0.0.1

Première version lançable. Elle pose le socle sur lequel tout le reste se greffe :

- **Serveur** ASP.NET Core 8 + EF Core (SQLite par défaut, PostgreSQL en un changement
  de chaîne de connexion). Déployable en conteneur sur Proxmox ou n'importe quelle machine.
- **Inscription des postes façon GLPI** : l'admin génère un code d'inscription, l'agent
  le présente, reçoit une identité et un jeton d'agent (stocké haché côté serveur).
- **Agent multiplateforme** (Windows / macOS / Linux) : inventaire de base, boucle de
  heartbeat, récupération d'ordres (verrouiller, redémarrer, message, ticket, auto-login).
- **Détection de session RDP** (Windows) : tant qu'une session distante est active,
  l'agent suspend la connexion automatique de session.
- **Console d'administration** via API (clé admin) : création de codes, liste des postes
  avec état en ligne, mise en file de commandes.

> Le suivi détaillé d'activité des applications n'est pas encore branché : il sera ajouté
> composant par composant, avec information des utilisateurs et mode privé (voir RGPD).

## Structure

```
Remina.sln
src/
  Remina.Shared/   DTO et contrats partagés agent <-> serveur, versionnage
  Remina.Server/   API ASP.NET Core : inscription, heartbeat, admin
  Remina.Agent/    Service multiplateforme : inscription + heartbeat + plateformes
```

## Télécharger une version prête à lancer

Des exécutables **autonomes** (aucun .NET à installer sur la machine cible) sont produits
automatiquement par GitHub Actions :

- **Builds à jour** : onglet *Actions* → dernier run *Build & Release* → section *Artifacts*.
  ZIP par plateforme : `Remina-Server-*` et `Remina-Agent-*` (`win-x64`, `linux-x64`,
  `osx-x64`, `osx-arm64`).
- **Versions publiées** : pousser un tag `vX.Y.Z` crée une *Release* avec les ZIP attachés.

```bash
git tag v0.0.1 && git push origin v0.0.1   # déclenche la Release 0.0.1
```

Lancer après décompression :

```bash
# Linux / macOS
./Remina.Server        # serveur
./Remina.Agent         # agent

# Windows
Remina.Server.exe
Remina.Agent.exe
```

## Console web

Le serveur sert une console d'administration à sa racine (`http://SERVER:5080/`) :
postes (état en ligne/RDP, utilisateur, rattachement à un bureau, commandes), bureaux
(liste/création) et génération de codes d'inscription. Elle s'authentifie avec la clé
admin (`X-Admin-Key`), conservée le temps de la session du navigateur. La liste des postes
se rafraîchit toute seule toutes les 20 s.

## Démarrer le serveur (depuis les sources)

```bash
cd src/Remina.Server
# définir une vraie clé admin
export Admin__ApiKey="ma-cle-admin"
dotnet run --urls "http://0.0.0.0:5080"
```

Vérifier : `curl http://localhost:5080/` renvoie le nom et la version.

## Inscrire un poste

1. Créer un code d'inscription :

```bash
curl -X POST http://localhost:5080/api/admin/enrollment-codes \
  -H "X-Admin-Key: ma-cle-admin" -H "Content-Type: application/json" \
  -d '{"label":"Bureau","maxUses":5,"expiresInHours":24}'
```

2. Lancer l'agent avec ce code :

```bash
cd src/Remina.Agent
export Remina__ServerUrl="http://IP_DU_SERVEUR:5080"
export Remina__EnrollmentCode="LE-CODE-RENVOYE"
dotnet run
```

L'agent s'inscrit une fois, enregistre son identité dans `remina-agent.json`, puis envoie
un heartbeat toutes les 30 s. Le poste apparaît dans `GET /api/admin/machines`.

## Endpoints

| Méthode | Route | Auth | Rôle |
|---|---|---|---|
| GET | `/` `/health` | — | Version, état |
| POST | `/api/agent/enroll` | code | Inscription d'un poste |
| POST | `/api/agent/heartbeat` | jeton agent | État + récupération d'ordres |
| POST | `/api/admin/enrollment-codes` | clé admin | Créer un code d'inscription (option `bureauId`) |
| GET | `/api/admin/machines` | clé admin | Lister les postes (avec leur bureau) |
| POST | `/api/admin/machines/{id}/commands` | clé admin | Mettre un ordre en file |
| POST | `/api/admin/bureaus` | clé admin | Créer un bureau (local) |
| GET | `/api/admin/bureaus` | clé admin | Lister les bureaux + nombre de postes |
| GET | `/api/admin/bureaus/{id}/machines` | clé admin | Postes d'un bureau |
| POST | `/api/admin/machines/{id}/bureau` | clé admin | Rattacher/détacher un poste d'un bureau |

Types d'ordres : `1` verrouiller, `2` redémarrer, `3` message, `4` ticket récapitulatif,
`5` connexion automatique.

## Bureaux (locaux)

Un **bureau** = un local = un groupe de postes (comme les entités/lieux de GLPI). Un poste
appartient à au plus un bureau. Deux façons de rattacher :

- **À l'inscription** : créer le code avec un `bureauId` ; tout poste inscrit avec ce code
  rejoint ce bureau automatiquement.
- **Après coup** : `POST /api/admin/machines/{id}/bureau` avec `{"bureauId":"..."}`
  (ou `null` pour détacher).

```bash
# 1. créer le bureau
curl -X POST http://SERVER:5080/api/admin/bureaus \
  -H "X-Admin-Key: ma-cle-admin" -H "Content-Type: application/json" \
  -d '{"name":"Bureau Salon","location":"Maison - salon"}'
# 2. code d'inscription lié à ce bureau
curl -X POST http://SERVER:5080/api/admin/enrollment-codes \
  -H "X-Admin-Key: ma-cle-admin" -H "Content-Type: application/json" \
  -d '{"label":"Salon","maxUses":5,"expiresInHours":24,"bureauId":"<ID_DU_BUREAU>"}'
```

## Installateurs

Produits par le workflow CI (artefacts, et Release sur tag) :

- **Windows** : `Remina-Server-Setup.exe` / `Remina-Agent-Setup.exe` (Inno Setup). Ils
  demandent l'URL du serveur / la clé admin, écrivent la config et enregistrent un
  **service Windows**.
- **Linux** : ZIP `*-linux-x64` contenant le binaire, l'unité systemd et `install.sh` :
  `sudo ./install.sh server` ou `sudo ./install.sh agent` (service systemd, config guidée).
- **macOS** : ZIP `Remina-Agent-osx-*` avec `install.sh` et un LaunchDaemon :
  `sudo ./install.sh`.

## Mise à jour automatique

Le serveur et l'agent se mettent à jour seuls à partir des **Releases GitHub** :

- Le serveur interroge la dernière Release (en cache) et expose
  `GET /api/update?app=agent|server&rid=...&current=...`.
- Chaque agent demande au serveur s'il existe une version plus récente (toutes les 6 h
  par défaut) ; le serveur vérifie aussi sa propre version.
- Si oui : téléchargement de l'archive du bon RID → mise en attente dans `update/` →
  un court script d'aide attend l'arrêt du processus, recopie les fichiers (sans toucher
  à `appsettings.json`) et redémarre le service.

Réglages (config ou variables `Remina__Update__*`) :

| Clé | Défaut | Rôle |
|---|---|---|
| `Remina:Update:Enabled` | `true` | Active/désactive l'auto-update |
| `Remina:Update:Repo` | `AngelGitHub17/Remina` | Dépôt source des Releases |
| `Remina:Update:CheckHours` | `6` | Intervalle de vérification |
| `Remina:Update:CacheMinutes` | `30` | Cache de l'info de Release (serveur) |
| `Remina:Update:ServiceName` | auto | Nom du service à redémarrer |

> Les unités systemd/launchd et le service Windows sont réglés pour laisser le script de
> bascule survivre à l'arrêt du binaire et ne pas redémarrer en double.

## Proxmox (script LXC)

Sur l'hôte Proxmox VE, en root — crée un conteneur Debian 12 et y installe le serveur :

```bash
bash -c "$(curl -fsSL https://raw.githubusercontent.com/AngelGitHub17/Remina/main/ct/remina.sh)"
```

Paramètres surchargeables en variables d'environnement : `CTID`, `RAM_MB`, `CORES`,
`DISK_GB`, `BRIDGE`, `STORAGE`, `PORT`, `ADMIN_KEY`. Le script récupère le binaire depuis
la dernière **Release** du dépôt : publie d'abord un tag `vX.Y.Z`.

Mettre à jour le serveur du conteneur (depuis l'hôte Proxmox) :

```bash
bash -c "$(curl -fsSL https://raw.githubusercontent.com/AngelGitHub17/Remina/main/ct/remina-update.sh)"
```

Conteneur auto-détecté (nommé `remina`) ou `CTID=<id>`. La config (`/etc/remina`) et la
base (`remina.db`) sont conservées. À partir de la v0.0.3, le serveur se met de toute façon
à jour seul (vérification toutes les 6 h).

## Pile par plateforme (agent)

- **Windows** : identifiant via `MachineGuid`, RDP via `SM_REMOTESESSION`, inactivité via
  `GetLastInputInfo`.
- **Linux** : identifiant via `/etc/machine-id`. Inactivité sous Wayland à venir (extension bureau).
- **macOS** : identifiant via `IOPlatformUUID`. Suivi détaillé soumis à l'autorisation d'accessibilité.

## Suite prévue

Suivi d'activité (transparent, avec mode privé) → ticket récapitulatif A4 (impression auto
après 5 min sans réponse) → connexion automatique de session → temps réel SignalR →
console web + NounoursAccount.

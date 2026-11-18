# Remina — État du projet & feuille de route

Logiciel de gestion de parc client/serveur multiplateforme (façon GLPI), en .NET 8.
Dépôt : https://github.com/AngelGitHub17/Remina

## Convention de version
Série `0.x.x` = nom de code **Remina** (le nom ne change qu'au passage à `1.0.0`, puis
`2.0.0`…). Chaque itération poussée = une nouvelle version. Format : `X.Y.Z-AAAAMMJJ-<Nom>`.
**Publier = créer un tag `vX.Y.Z`** sur GitHub → la CI construit et attache les archives et
installateurs à une Release ; l'auto-mise à jour s'appuie dessus.

## Architecture
- `src/Remina.Shared` — contrats agent/serveur, versionnage, auto-updater, impression, installeur.
- `src/Remina.Server` — API ASP.NET Core + EF Core (SQLite, `SchemaGuard` crée les tables
  manquantes au démarrage), SignalR (`/hubs/agent`), console web (`wwwroot/`), auto-update serveur.
- `src/Remina.Agent` — agent **service** (Worker, Windows service / systemd / launchd).
- `src/Remina.Agent.Tray` — agent **graphique** Avalonia (icône barre des tâches), réutilise le
  cœur de `Remina.Agent` via un hôte de fond.
- `packaging/` — installateurs (Inno Setup Windows, systemd Linux, launchd macOS).
- `ct/` — scripts Proxmox : `remina.sh` (création LXC), `remina-update.sh` (mise à jour serveur).
- `.github/workflows/release.yml` — build 4 plateformes + installateurs Windows, Release sur tag.

## Nouveautés v0.1.12
- **Correctif routage des commandes** : les postes clonés (même `MachineGuid` Windows)
  recevaient le même identifiant matériel → commandes/installeur routés vers le mauvais poste.
  `GetHardwareId` combine désormais base OS + adresse MAC + nom de machine (hash SHA-256),
  garantissant un identifiant unique par poste.
- **Agent graphique : bouton « Étape du récap »** — saisir le libellé, démarrer/arrêter un
  chrono ; à l'arrêt (≥ 30 s) le temps est envoyé au serveur (`POST /api/agent/time-entry`,
  source « agent · <poste> »), comme la page mobile `/log.html`.
- **Installateurs simples pour les autres OS** (agent, interface comprise) :
  - Linux : **.deb** et **.rpm** (agent service, via nfpm) → `packaging/linux/nfpm/`.
  - Linux : **AppImage** (agent graphique) → `packaging/linux/appimage/`.
  - macOS : **.dmg** (agent graphique, `Remina Agent.app` signé ad-hoc) → `packaging/macos/make-dmg.sh`.
  - CI : jobs `linux-installers` (ubuntu) et `macos-installer` (macOS) ; artefacts attachés à la Release.

## Fait (jusqu'à v0.1.11)
- Inscription des postes façon GLPI (codes d'inscription), heartbeat, temps réel SignalR.
- Bureaux (groupes de postes = locaux), rattachement à l'inscription ou après coup.
- Détection RDP (pause auto-login), suivi d'inactivité.
- Commandes réelles : verrouiller, redémarrer, message, ticket, auto-login, installer un paquet.
- Suivi d'activité (appli au premier plan, idle-aware, mode privé, titres off par défaut).
- Saisie manuelle de temps + page mobile `/log.html` ; ticket récap imprimable `/recap.html`
  + impression auto (dialogue 5 min / imprimante par défaut, planificateur horaire).
- Auto-login sécurisé (secret LSA + UAC), choix d'imprimante mémorisé.
- Console admin en sidebar : tableau de bord (KPIs, graphes), recherche, détail poste
  (inventaire, activité, historique commandes avec résultat), bureaux, activité, logiciels,
  récap, codes.
- Déploiement de logiciels (upload installeur, déploiement poste ou bureau, installation
  silencieuse par OS, retour de statut).
- Inventaire logiciel (registre Windows / dpkg-rpm Linux / /Applications macOS).
- Wake-on-LAN.
- Auto-mise à jour serveur + agents (service et graphique) depuis les Releases GitHub,
  par RID et par variante ; serveur Proxmox autorisé à redémarrer (sudoers).

## Points d'attention connus
- Auto-login et installations système nécessitent l'agent **service** (LocalSystem/root) ;
  l'agent graphique en session utilisateur peut échouer sur ces actions privilégiées.
- Derrière un reverse proxy (NPM), activer **WebSockets** pour le temps réel SignalR.
- Un agent/serveur sur une version **antérieure au correctif d'auto-update (v0.1.11)** doit
  être mis à jour **manuellement une fois** (réinstaller l'agent, lancer `ct/remina-update.sh`
  pour le serveur) ; ensuite l'auto-update s'enchaîne seul.

## Feuille de route v0.2.0 (à décider / construire)
- **A. Comptes & sécurité** : vrais comptes admin + rôles (admin / lecture seule), fin de la
  clé unique, connexion via **NounoursAccount** (SSO existant), journal d'audit.
- **B. Supervision & alertes** : métriques système (CPU/RAM/disque/température) + historique
  et graphes ; alertes (poste hors ligne, disque plein, échec d'install) → mail / Discord
  (NounoursBot).
- **C. Contrôle à distance** : prise de main écran et/ou shell distant (sensible, à encadrer).
- **D. Rapports & politiques** : exports CSV/PDF, rapports planifiés, tendances multi-semaines ;
  politiques de bureau (un poste rejoignant un bureau installe automatiquement son jeu de logiciels).

Ordre conseillé : A (conditionne le multi-utilisateur) → B (valeur quotidienne) → D → C.

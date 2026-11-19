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

## Nouveautés v0.2.2 — Connexion NounoursAccount (axe A, itération 3)
- Bouton **« Se connecter avec NounoursAccount »** : redirection vers
  `didikaria.fr/connexion.php`, retour avec le `sso_token` existant (signé, 60 s, usage
  unique), **échange serveur à serveur** auprès de `www/api/remina-sso.php` (dépôt
  dkparkofwebOVH) authentifié par secret partagé. Remina n'accède jamais à la base MySQL.
- Accès réservé aux **admins NounoursAccount** (`role_code ≥ 2`, réglable) ; compte Remina
  créé automatiquement en **lecture seule** à la première connexion (promotion manuelle).
- **Lier / délier** son compte local dans *Mon compte* (pas de liaison automatique par pseudo).
- **Déconnexion couplée** : chaque session SSO porte un « lien » (hachage d'une entrée
  `na_auth_tokens`, sans jeton brut) vérifié toutes les 2 min ; déconnexion, bannissement ou
  perte du rôle admin côté NounoursAccount → session Remina coupée (tolérance 30 min si
  NounoursAccount est injoignable). La déconnexion Remina révoque le lien.
- État de connexion (`state`) lié au navigateur (cookie SameSite=Lax) ; tous les échecs
  sont journalisés (`auth.nounours`). Colonnes `AdminSessions.ExternalLink` / `ExternalCheckedAt`.

## Nouveautés v0.2.1 — Journal d'audit & jetons d'API (axe A, itération 2)
- **Journal d'audit** (table `AuditLog`, colonne entière `AtUnixMs` pour trier/paginer en SQL) :
  - automatique pour **toute écriture** sur `/api/admin/*` (y compris les refus 403 d'un compte
    en lecture seule) : qui, action (méthode + route), cible, détail (corps JSON **expurgé** :
    mots de passe/clés/jetons masqués, charge d'auto-login masquée), IP, code HTTP ;
  - explicite pour connexions réussies/échouées (identifiant inconnu, mauvais mot de passe,
    compte verrouillé/désactivé), déconnexion, première configuration, changement de mot de
    passe, `--reset-admin`, jetons créés/révoqués, inscriptions de postes ;
  - vue **Journal** dans la console (admin) : filtres utilisateur / action / période / échecs,
    libellés lisibles, pagination « Plus ancien… » ;
  - **purge automatique** quotidienne au-delà de `Remina:Audit:RetentionDays` (365 j).
- **Jetons d'API personnels** (table `ApiTokens`, hachés) : `Authorization: Bearer rmn_…`,
  droits du compte, expiration au choix, créés/révoqués depuis **Mon compte** (session
  uniquement : un jeton ne peut pas en créer d'autres). Remplacent la clé historique.

## Nouveautés v0.2.0 — Comptes & sécurité (axe A, itération 1)
- **Comptes de console** (table `AdminUsers`) : identifiant, nom, mot de passe haché
  PBKDF2-SHA256 600 000 itérations (rehachage transparent si le coût augmente), rôles
  **Admin** / **Lecture seule** (consultation + saisie de temps). Colonnes `ExternalProvider`
  / `ExternalId` réservées au SSO NounoursAccount.
- **Sessions cookie** (table `AdminSessions`, seul le hachage du jeton est stocké) :
  `HttpOnly`, `SameSite=Strict`, `Secure` en HTTPS ; 12 h d'inactivité / 7 j max ; révocables
  (déconnexion, changement de mot de passe, compte désactivé). Anti-CSRF : en-tête `X-Remina: 1`.
- **Protection centralisée** : `AdminAuthMiddleware` protège tout `/api/admin/*` (les 28
  vérifications `IsAdmin` recopiées ont disparu) ; écriture = admin par défaut.
- **Migration de la clé** : sans compte, la console propose « Première configuration »
  (preuve = `Admin:ApiKey`). La clé `X-Admin-Key` reste acceptée pour les scripts jusqu'à
  sa désactivation (Comptes, table `ServerSettings`, ou `Admin:LegacyKeyEnabled=false`).
  Recours : `Remina.Server --reset-admin <identifiant>`.
- **Durcissement** : verrouillage 15 min après 5 échecs, 10 tentatives/min/IP, en-têtes CSP /
  X-Frame-Options / nosniff, `no-store` sur l'API, proxys de confiance
  (`Remina:TrustedProxies`) pour l'IP réelle et le HTTPS derrière Nginx.
- `/log.html` a sa propre connexion par compte ; `/recap.html` utilise la session de la console.

## Nouveautés v0.1.15
- **Correctif « poste désinscrit après redémarrage »** (régression v0.1.12) : l'identifiant
  matériel hachait la MAC de la *première carte « Up »*. Au démarrage, une carte virtuelle
  (Hyper-V `vEthernet`, VPN…) pouvait être montée avant l'Ethernet/Wi-Fi → identifiant
  différent → l'agent se croyait cloné, effaçait son identité et bouclait sur une
  réinscription (code épuisé), sans plus jamais remonter ni s'auto-mettre à jour.
  - L'identifiant n'est plus recalculé à chaque démarrage : il est figé à l'inscription.
  - Contrôle d'identité par **empreinte** (base OS + MAC *physiques* quel que soit leur état,
    hors cartes virtuelles et MAC aléatoires + nom d'hôte) : même poste si même base OS et au
    moins une MAC commune. Un clone (même MachineGuid, autres cartes) reste bien distingué.
  - Les états écrits par 0.1.12–0.1.14 sont reconnus (toutes les MAC sont essayées).
  - **Postes déjà bloqués** : ils ne peuvent pas s'auto-mettre à jour → réinstaller une fois
    l'agent 0.1.15 par-dessus ; l'état local (conservé) est reconnu et le poste réapparaît
    sous son ancienne fiche, sans doublon.

## Nouveautés v0.1.14
- **Correctif `make-dmg.sh`** : la récupération du point de montage via `awk` était cassée
  (regex invalide après interpolation du chemin) → `illegal primary in regular expression`.
  L'image est désormais montée sur un point de montage explicite (`hdiutil attach -mountpoint`),
  sans aucun parsing.

## Nouveautés v0.1.13
- **Correctif CI installeur macOS** : `hdiutil create -srcfolder` (avec lien `/Applications`)
  sur-estimait la taille de l'image → échec aléatoire `No space left on device` selon le
  runner. `make-dmg.sh` crée désormais une image RW explicitement dimensionnée (taille de
  l'app + 80 Mo) puis la convertit en UDZO ; la CI libère l'espace entre les deux architectures.
  (La v0.1.12 n'a pas produit de Release : le job macOS échouait, donc le job `release` était sauté.)

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
- Un agent **0.1.12–0.1.14 bloqué en réinscription** (après redémarrage) doit être réinstallé
  une fois en 0.1.15 (voir ci-dessus).
- Un agent/serveur sur une version **antérieure au correctif d'auto-update (v0.1.11)** doit
  être mis à jour **manuellement une fois** (réinstaller l'agent, lancer `ct/remina-update.sh`
  pour le serveur) ; ensuite l'auto-update s'enchaîne seul.

## Axe A — terminé (v0.2.0 → v0.2.2)
Prochaine étape conseillée : **axe B (supervision & alertes)**.

## Idées pour la console (panel)
- **Vue Postes** : filtres rapides (en ligne / hors ligne / RDP / bureau / OS / version
  d'agent obsolète), tri par colonne, sélection multiple + actions groupées (verrouiller,
  message, déployer), export CSV.
- **Fiche poste enrichie** : onglets (Inventaire, Activité, Commandes, Logiciels,
  Historique), notes libres / étiquettes, n° d'inventaire, date d'achat & garantie,
  utilisateur attitré, historique « vu en ligne » (frise de disponibilité 7 j).
- **Tableau de bord** : postes hors ligne depuis > X jours, agents à mettre à jour, disque
  presque plein (avec l'axe B), dernières commandes en échec, graphe d'activité sur 7 jours.
- **Recherche globale** (Ctrl+K) : postes, logiciels installés (« qui a encore Chrome 118 ? »),
  utilisateurs, bureaux.
- **Comparaison logicielle** : matrice logiciel × poste, écarts entre postes d'un même bureau.
- **Mode sombre/clair** et densité compacte ; vue mobile utilisable (sidebar repliable).
- **Notifications temps réel** dans la console (poste qui se connecte, commande terminée)
  via un hub SignalR « console ».
- **Plan des bureaux** : vue par bureau avec ses postes en cartes (état, utilisateur).
- **Planification de commandes** : redémarrage/verrouillage programmé, par poste ou bureau.
- **Préférences par compte** : vue d'accueil, colonnes affichées, fuseau horaire.

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

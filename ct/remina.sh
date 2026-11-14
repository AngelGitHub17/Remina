#!/usr/bin/env bash
# =====================================================================
#  Remina Server — script d'installation LXC pour Proxmox VE
#  À lancer sur l'HÔTE Proxmox, en root :
#     bash -c "$(curl -fsSL https://raw.githubusercontent.com/AngelGitHub17/Remina/main/ct/remina.sh)"
#
#  Crée un conteneur Debian 12, y installe le serveur Remina (binaire
#  autonome, aucun .NET requis) en service systemd, et l'expose en HTTP.
#  Style inspiré des Proxmox VE Community Scripts, mais autonome.
# =====================================================================
set -euo pipefail

# ------- apparence -------
RD=$'\033[01;31m'; GN=$'\033[1;92m'; YW=$'\033[33m'; BL=$'\033[36m'; CL=$'\033[m'
msg()  { echo -e " ${GN}✓${CL} $1"; }
info() { echo -e " ${BL}»${CL} $1"; }
warn() { echo -e " ${YW}!${CL} $1"; }
die()  { echo -e " ${RD}✗ $1${CL}" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "À lancer en root sur l'hôte Proxmox."
command -v pct >/dev/null || die "Commande 'pct' absente : ce script s'exécute sur un hôte Proxmox VE."

# ------- paramètres (surchargables par variables d'environnement) -------
CTID="${CTID:-$(pvesh get /cluster/nextid)}"
HOSTNAME_="${HOSTNAME_:-remina}"
DISK_GB="${DISK_GB:-4}"
CORES="${CORES:-2}"
RAM_MB="${RAM_MB:-1024}"
BRIDGE="${BRIDGE:-vmbr0}"
STORAGE="${STORAGE:-local-lvm}"
TMPL_STORAGE="${TMPL_STORAGE:-local}"
PORT="${PORT:-5080}"
ADMIN_KEY="${ADMIN_KEY:-$(head -c16 /dev/urandom | od -An -tx1 | tr -d ' \n')}"
REPO="AngelGitHub17/Remina"
ASSET="Remina-Server-linux-x64.zip"

info "Conteneur CTID=${CTID}, hôte=${HOSTNAME_}, ${CORES} vCPU / ${RAM_MB} Mo / ${DISK_GB} Go, pont=${BRIDGE}"

# ------- modèle Debian (nom résolu dynamiquement depuis le catalogue) -------
info "Recherche du modèle Debian 12…"
pveam update >/dev/null 2>&1 || true
# Un modèle déjà téléchargé est réutilisé ; sinon on prend le plus récent du catalogue.
TEMPLATE="${TEMPLATE:-$(pveam list "$TMPL_STORAGE" 2>/dev/null | awk '/debian-12-standard/{print $1}' | sed 's#.*/##' | sort -V | tail -1)}"
if [[ -z "$TEMPLATE" ]]; then
  TEMPLATE="$(pveam available --section system 2>/dev/null | awk '/debian-12-standard/{print $NF}' | sort -V | tail -1)"
  [[ -n "$TEMPLATE" ]] || die "Aucun modèle 'debian-12-standard' dans le catalogue (essaie: pveam available)."
  info "Téléchargement du modèle $TEMPLATE…"
  pveam download "$TMPL_STORAGE" "$TEMPLATE" || die "Échec du téléchargement du modèle Debian 12."
else
  msg "Modèle déjà présent : $TEMPLATE"
fi

# ------- création du conteneur -------
info "Création du conteneur…"
pct create "$CTID" "${TMPL_STORAGE}:vztmpl/${TEMPLATE}" \
  --hostname "$HOSTNAME_" \
  --cores "$CORES" --memory "$RAM_MB" \
  --rootfs "${STORAGE}:${DISK_GB}" \
  --net0 "name=eth0,bridge=${BRIDGE},ip=dhcp" \
  --features nesting=1 \
  --unprivileged 1 \
  --onboot 1 >/dev/null
msg "Conteneur $CTID créé."

pct start "$CTID"; sleep 5
msg "Conteneur démarré."

# ------- installation à l'intérieur -------
run() { pct exec "$CTID" -- bash -c "$1"; }

info "Installation des dépendances…"
run "apt-get update -qq && apt-get install -y -qq curl unzip ca-certificates libicu72 >/dev/null"

info "Récupération du serveur Remina (dernière Release)…"
DL=$(run "curl -fsSL https://api.github.com/repos/${REPO}/releases/latest | grep browser_download_url | grep ${ASSET} | head -1 | cut -d'\"' -f4" || true)
if [[ -z "$DL" ]]; then
  warn "Aucune Release trouvée avec l'asset ${ASSET}."
  warn "Publie d'abord une Release (tag vX.Y.Z) sur le dépôt, puis relance ce script."
  die "Installation interrompue."
fi
run "mkdir -p /opt/remina/server && curl -fsSL '$DL' -o /tmp/server.zip && unzip -oq /tmp/server.zip -d /opt/remina/server && chmod +x /opt/remina/server/Remina.Server"

info "Configuration et service…"
run "id -u remina >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin remina"
run "mkdir -p /etc/remina && printf 'Admin__ApiKey=%s\nUrls=http://0.0.0.0:%s\n' '$ADMIN_KEY' '$PORT' > /etc/remina/server.env && chmod 600 /etc/remina/server.env"
run "chown -R remina:remina /opt/remina/server"
run "cat > /etc/systemd/system/remina-server.service <<'UNIT'
[Unit]
Description=Remina Server
After=network-online.target
Wants=network-online.target
[Service]
Type=notify
WorkingDirectory=/opt/remina/server
ExecStart=/opt/remina/server/Remina.Server
EnvironmentFile=-/etc/remina/server.env
Restart=on-failure
RestartSec=5
User=remina
Group=remina
[Install]
WantedBy=multi-user.target
UNIT"
run "systemctl daemon-reload && systemctl enable --now remina-server >/dev/null 2>&1"

IP=$(run "hostname -I | awk '{print \$1}'" | tr -d '\r')

echo
msg "Remina Server installé dans le conteneur ${CTID}."
echo -e "   ${BL}URL${CL}        : http://${IP}:${PORT}/"
echo -e "   ${BL}Clé admin${CL}  : ${ADMIN_KEY}"
echo -e "   ${BL}En-tête${CL}    : X-Admin-Key: ${ADMIN_KEY}"
echo
info "Créer un code d'inscription :"
echo "   curl -X POST http://${IP}:${PORT}/api/admin/enrollment-codes \\"
echo "     -H 'X-Admin-Key: ${ADMIN_KEY}' -H 'Content-Type: application/json' \\"
echo "     -d '{\"label\":\"Bureau\",\"maxUses\":5,\"expiresInHours\":24}'"

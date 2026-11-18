#!/usr/bin/env bash
# =====================================================================
#  Remina Server — mise à jour du conteneur LXC sur Proxmox
#  À lancer sur l'HÔTE Proxmox, en root :
#     bash -c "$(curl -fsSL https://raw.githubusercontent.com/AngelGitHub17/Remina/main/ct/remina-update.sh)"
#
#  Télécharge la dernière Release du serveur et la met en place dans le
#  conteneur, en conservant la config (/etc/remina) et la base (remina.db).
#  CTID auto-détecté (conteneur nommé « remina ») ou passé en variable : CTID=115 ...
# =====================================================================
set -euo pipefail

RD=$'\033[01;31m'; GN=$'\033[1;92m'; BL=$'\033[36m'; CL=$'\033[m'
msg(){ echo -e " ${GN}✓${CL} $1"; }
info(){ echo -e " ${BL}»${CL} $1"; }
die(){ echo -e " ${RD}✗ $1${CL}" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "À lancer en root sur l'hôte Proxmox."
command -v pct >/dev/null || die "Commande 'pct' absente : ce script s'exécute sur un hôte Proxmox VE."

REPO="${REPO:-AngelGitHub17/Remina}"
ASSET="Remina-Server-linux-x64.zip"

# Conteneur : CTID fourni, sinon on cherche celui nommé « remina ».
if [[ -z "${CTID:-}" ]]; then
  CTID="$(pct list | awk 'NR>1 && $NF=="remina"{print $1; exit}')"
  [[ -n "$CTID" ]] || die "Conteneur 'remina' introuvable. Précise-le : CTID=<id> bash -c \"\$(curl …)\""
fi
info "Conteneur cible : $CTID"

pct status "$CTID" | grep -q running || { info "Démarrage du conteneur…"; pct start "$CTID"; sleep 4; }

info "Mise à jour du serveur depuis la dernière Release de $REPO…"
pct exec "$CTID" -- bash -c '
set -e
command -v unzip >/dev/null || { apt-get update -qq && apt-get install -y -qq unzip curl >/dev/null; }
DL=$(curl -fsSL https://api.github.com/repos/'"$REPO"'/releases/latest \
     | grep browser_download_url | grep '"$ASSET"' | head -1 | cut -d"\"" -f4)
[ -n "$DL" ] || { echo "Aucune Release avec '"$ASSET"'."; exit 1; }
echo "   -> $DL"
curl -fsSL "$DL" -o /tmp/remina-server.zip
systemctl stop remina-server 2>/dev/null || true
# -o : écrase les fichiers du binaire, ne touche pas à remina.db ni à /etc/remina.
unzip -oq /tmp/remina-server.zip -d /opt/remina/server
chmod +x /opt/remina/server/Remina.Server
id -u remina >/dev/null 2>&1 && chown -R remina:remina /opt/remina/server || true
rm -f /tmp/remina-server.zip
systemctl start remina-server
'
# Autorise 'remina' à redémarrer son service (pour l'auto-mise à jour suivante).
pct exec "$CTID" -- bash -c "command -v sudo >/dev/null 2>&1 || { apt-get update -qq; apt-get install -y -qq sudo; }; printf 'remina ALL=(root) NOPASSWD: /usr/bin/systemctl restart remina-server, /bin/systemctl restart remina-server\n' > /etc/sudoers.d/remina && chmod 440 /etc/sudoers.d/remina" >/dev/null 2>&1 || true

msg "Serveur mis à jour."
pct exec "$CTID" -- bash -c 'sleep 2; curl -fsS http://127.0.0.1:5080/api/version || true'
echo
msg "Terminé. Les mises à jour suivantes se feront automatiquement (vérification toutes les 6 h)."

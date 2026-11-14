#!/usr/bin/env bash
# Installe le serveur ou l'agent Remina en service systemd sur Linux.
# À lancer en root depuis le dossier décompressé contenant le binaire et le .service.
#   sudo ./install.sh server    (ou)    sudo ./install.sh agent
set -euo pipefail

ROLE="${1:-}"
if [[ "$ROLE" != "server" && "$ROLE" != "agent" ]]; then
  echo "Usage : sudo ./install.sh [server|agent]" >&2
  exit 1
fi

if [[ $EUID -ne 0 ]]; then
  echo "Ce script doit être lancé en root (sudo)." >&2
  exit 1
fi

SRC="$(cd "$(dirname "$0")" && pwd)"
DEST="/opt/remina/$ROLE"
BIN="Remina.$( [[ "$ROLE" == "server" ]] && echo Server || echo Agent )"

echo ">> Installation de Remina ($ROLE) dans $DEST"
install -d "$DEST" /etc/remina

# Copie des binaires (tout sauf les scripts/units de packaging).
for f in "$SRC"/*; do
  base="$(basename "$f")"
  case "$base" in
    install.sh|remina-*.service) continue ;;
    *) cp -a "$f" "$DEST/" ;;
  esac
done
chmod +x "$DEST/$BIN"

if [[ "$ROLE" == "server" ]]; then
  id -u remina &>/dev/null || useradd --system --no-create-home --shell /usr/sbin/nologin remina
  chown -R remina:remina "$DEST"
  if [[ ! -f /etc/remina/server.env ]]; then
    read -rp "Clé d'administration (X-Admin-Key) : " ADMINKEY
    read -rp "URL d'écoute [http://0.0.0.0:5080] : " URLS
    URLS="${URLS:-http://0.0.0.0:5080}"
    cat >/etc/remina/server.env <<EOF
Admin__ApiKey=$ADMINKEY
Urls=$URLS
EOF
    chmod 600 /etc/remina/server.env
  fi
else
  if [[ ! -f /etc/remina/agent.env ]]; then
    read -rp "URL du serveur [http://localhost:5080] : " SRVURL
    SRVURL="${SRVURL:-http://localhost:5080}"
    read -rp "Code d'inscription : " CODE
    cat >/etc/remina/agent.env <<EOF
Remina__ServerUrl=$SRVURL
Remina__EnrollmentCode=$CODE
EOF
    chmod 600 /etc/remina/agent.env
  fi
fi

install -m 644 "$SRC/remina-$ROLE.service" "/etc/systemd/system/remina-$ROLE.service"
systemctl daemon-reload
systemctl enable --now "remina-$ROLE"
echo ">> Terminé. État :"
systemctl --no-pager --full status "remina-$ROLE" | head -5 || true

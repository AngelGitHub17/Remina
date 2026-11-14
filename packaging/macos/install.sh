#!/usr/bin/env bash
# Installe l'agent Remina en LaunchDaemon sur macOS.
# À lancer en root depuis le dossier décompressé :  sudo ./install.sh
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "Ce script doit être lancé en root (sudo)." >&2
  exit 1
fi

SRC="$(cd "$(dirname "$0")" && pwd)"
DEST="/opt/remina/agent"
PLIST="fr.nounourscorp.remina.agent.plist"

echo ">> Installation de l'agent Remina dans $DEST"
mkdir -p "$DEST"
for f in "$SRC"/*; do
  base="$(basename "$f")"
  case "$base" in
    install.sh|*.plist) continue ;;
    *) cp -a "$f" "$DEST/" ;;
  esac
done
chmod +x "$DEST/Remina.Agent"

if [[ ! -f "$DEST/appsettings.json" ]]; then
  read -rp "URL du serveur [http://localhost:5080] : " SRVURL
  SRVURL="${SRVURL:-http://localhost:5080}"
  read -rp "Code d'inscription : " CODE
  cat >"$DEST/appsettings.json" <<EOF
{
  "Logging": { "LogLevel": { "Default": "Information" } },
  "Remina": { "ServerUrl": "$SRVURL", "EnrollmentCode": "$CODE", "IdleMinutes": 5 }
}
EOF
fi

cp "$SRC/$PLIST" "/Library/LaunchDaemons/$PLIST"
launchctl unload "/Library/LaunchDaemons/$PLIST" 2>/dev/null || true
launchctl load -w "/Library/LaunchDaemons/$PLIST"
echo ">> Terminé. L'agent est chargé (launchctl list | grep remina)."
echo ">> Note : le suivi détaillé exigera l'autorisation d'accessibilité (Réglages Système > Confidentialité)."

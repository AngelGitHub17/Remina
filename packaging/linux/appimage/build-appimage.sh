#!/usr/bin/env bash
# Construit l'AppImage de l'agent graphique Remina.
#   build-appimage.sh <dossier-publie-GUI> <version> [arch]
# Nécessite appimagetool dans le PATH (ou $APPIMAGETOOL).
set -euo pipefail
PUB="${1:?dossier publié requis}"
VERSION="${2:?version requise}"
ARCH="${3:-x86_64}"
HERE="$(cd "$(dirname "$0")" && pwd)"
APPDIR="$(mktemp -d)/Remina-Agent.AppDir"

mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" \
         "$APPDIR/usr/share/icons/hicolor/scalable/apps"
cp -a "$PUB"/. "$APPDIR/usr/bin/"
chmod +x "$APPDIR/usr/bin/Remina.Agent.Tray"
cp "$HERE/AppRun" "$APPDIR/AppRun"; chmod +x "$APPDIR/AppRun"
cp "$HERE/remina-agent.desktop" "$APPDIR/remina-agent.desktop"
cp "$HERE/remina-agent.desktop" "$APPDIR/usr/share/applications/"
cp "$HERE/remina-agent.svg" "$APPDIR/remina-agent.svg"
cp "$HERE/remina-agent.svg" "$APPDIR/usr/share/icons/hicolor/scalable/apps/remina-agent.svg"

TOOL="${APPIMAGETOOL:-appimagetool}"
OUT="Remina-Agent-${VERSION}-${ARCH}.AppImage"
ARCH="$ARCH" "$TOOL" --no-appstream "$APPDIR" "$OUT"
echo "AppImage: $OUT"

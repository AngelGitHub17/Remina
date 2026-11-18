#!/usr/bin/env bash
# Construit "Remina Agent.app" puis un .dmg (agent graphique). macOS uniquement.
#   make-dmg.sh <dossier-publie-GUI> <version> [arch]
set -euo pipefail
PUB="${1:?dossier publié requis}"
VERSION="${2:?version requise}"
ARCH="${3:-arm64}"
HERE="$(cd "$(dirname "$0")" && pwd)"
WORK="$(mktemp -d)"
APP="$WORK/Remina Agent.app"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -a "$PUB"/. "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/Remina.Agent.Tray"
sed "s/__VERSION__/$VERSION/g" "$HERE/Info.plist" > "$APP/Contents/Info.plist"

# Icône .icns depuis le SVG. Rasterisation via qlmanage (fourni par macOS) puis sips/iconutil.
# Si la conversion échoue, l'app garde l'icône par défaut (le .dmg se construit quand même).
SVG="$HERE/../linux/appimage/remina-agent.svg"
ICONSET="$WORK/remina-agent.iconset"; mkdir -p "$ICONSET"
BASE="$WORK/icon-1024.png"
if [ -f "$SVG" ]; then
  if command -v rsvg-convert >/dev/null 2>&1; then
    rsvg-convert -w 1024 -h 1024 "$SVG" -o "$BASE" 2>/dev/null || true
  elif command -v qlmanage >/dev/null 2>&1; then
    qlmanage -t -s 1024 -o "$WORK" "$SVG" >/dev/null 2>&1 || true
    [ -f "$WORK/remina-agent.svg.png" ] && mv "$WORK/remina-agent.svg.png" "$BASE"
  fi
fi
if [ -f "$BASE" ] && command -v sips >/dev/null 2>&1; then
  for s in 16 32 64 128 256 512; do
    sips -z $s $s "$BASE" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null 2>&1 || true
    d=$((s*2)); sips -z $d $d "$BASE" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null 2>&1 || true
  done
  iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/remina-agent.icns" 2>/dev/null || true
fi
[ -f "$APP/Contents/Resources/remina-agent.icns" ] || \
  /usr/libexec/PlistBuddy -c "Delete :CFBundleIconFile" "$APP/Contents/Info.plist" 2>/dev/null || true

# Signature ad-hoc pour éviter "fichier endommagé" sur le poste de l'utilisateur.
codesign --force --deep -s - "$APP" 2>/dev/null || true

OUT="Remina-Agent-GUI-${VERSION}-${ARCH}.dmg"
# Image RW explicitement dimensionnée (taille de l'app + marge) : évite la sur-estimation
# de `hdiutil create -srcfolder` (qui suit le lien /Applications) et les « No space left ».
RW="$WORK/rw-${ARCH}.dmg"
MNT="$WORK/mnt-${ARCH}"
SIZE_MB=$(( $(du -sm "$APP" | cut -f1) + 80 ))
hdiutil create -volname "Remina Agent" -size "${SIZE_MB}m" -fs HFS+ -layout SPUD -ov "$RW"
mkdir -p "$MNT"
hdiutil attach "$RW" -nobrowse -noautoopen -mountpoint "$MNT"
cp -a "$APP" "$MNT/"
ln -s /Applications "$MNT/Applications" 2>/dev/null || true
sync
hdiutil detach "$MNT" -force
hdiutil convert "$RW" -format UDZO -ov -o "$OUT"
rm -f "$RW"
echo "DMG: $OUT"

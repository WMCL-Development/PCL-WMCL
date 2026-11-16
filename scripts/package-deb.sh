#!/usr/bin/env bash
# 构建自包含的 arm64/x64 deb 包。
# 用法：scripts/package-deb.sh [rid] [version] [output-dir]
#
# 密钥：若存在 ~/.config/wmcl/pcl-ce-secrets.env 则自动加载，
# 其中的 PCL_MS_CLIENT_ID / PCL_CURSEFORGE_API_KEY 会在编译期编入程序集。
set -euo pipefail

RID="${1:-linux-arm64}"
VERSION="${2:-2.15.1-wmcl}"
OUT_DIR="${3:-/sdcard/Download/DSH-Go}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET="${DOTNET:-/root/opt/dotnet11/dotnet}"
SECRETS="${PCL_CE_SECRETS:-$HOME/.config/wmcl/pcl-ce-secrets.env}"

case "$RID" in
  linux-arm64) ARCH=arm64 ;;
  linux-x64)   ARCH=amd64 ;;
  *) echo "unsupported RID: $RID" >&2; exit 2 ;;
esac

if [ -f "$SECRETS" ]; then
  # shellcheck disable=SC1090
  . "$SECRETS"
  echo "secrets: 已从 $SECRETS 加载"
else
  echo "secrets: 未找到 $SECRETS，构建出的包不含正版登录与 CurseForge 密钥"
fi

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

PUBLISH="$STAGE/publish"
"$DOTNET" publish "$ROOT/PCL.Launcher/PCL.Launcher.csproj" \
  -c Release -r "$RID" --self-contained true -o "$PUBLISH" --nologo

PKG="$STAGE/pkg"
mkdir -p "$PKG/DEBIAN" "$PKG/opt/pcl-ce" "$PKG/usr/bin" \
         "$PKG/usr/share/applications" "$PKG/usr/share/icons/hicolor/256x256/apps"
cp -a "$PUBLISH/." "$PKG/opt/pcl-ce/"

cat > "$PKG/DEBIAN/control" <<EOF
Package: pcl-ce
Version: $VERSION
Section: games
Priority: optional
Architecture: $ARCH
Maintainer: wenxiangL521 <wenxiangL521@users.noreply.github.com>
Description: Plain Craft Launcher CE (WMCL port)
 Minecraft launcher with Terracotta, Astral and EasyTier multiplayer,
 built-in help, skin editor, pixel achievement generator and a
 creative-inventory style collection library.
 Self-contained build, no .NET runtime required.
EOF

cat > "$PKG/usr/bin/pcl-ce" <<'EOF'
#!/bin/sh
export PCL_NO_ELEVATION=1
exec /opt/pcl-ce/PCL.Launcher "$@"
EOF
chmod 755 "$PKG/usr/bin/pcl-ce"

cat > "$PKG/usr/share/applications/pcl-ce.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=PCL CE
Comment=Minecraft launcher
Exec=pcl-ce
Terminal=false
Categories=Game;
Icon=pcl-ce
EOF

ICON="$ROOT/PCL.Launcher/Assets/Images/Icons/DefaultServer.png"
[ -f "$ICON" ] && cp "$ICON" "$PKG/usr/share/icons/hicolor/256x256/apps/pcl-ce.png"

find "$PKG" -type d -exec chmod 755 {} +
find "$PKG" -type f -exec chmod 644 {} +
chmod 755 "$PKG/DEBIAN" "$PKG/opt/pcl-ce/PCL.Launcher" "$PKG/usr/bin/pcl-ce"

DEB="$OUT_DIR/pcl-ce_${VERSION}_${ARCH}.deb"
mkdir -p "$OUT_DIR"
dpkg-deb --build --root-owner-group "$PKG" "$DEB" >/dev/null
chmod 644 "$DEB" 2>/dev/null || true
echo "built: $DEB"
sha256sum "$DEB" | cut -c1-16

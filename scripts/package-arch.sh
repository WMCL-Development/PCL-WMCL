#!/usr/bin/env bash
# 生成 Arch Linux 软件包（.pkg.tar.zst），无需 makepkg。
# 用法：scripts/package-arch.sh <x86_64|aarch64> <version> <publish-dir> <output-dir> <asset-prefix>
set -euo pipefail

ARCH="${1:?usage: package-arch.sh <x86_64|aarch64> <version> <publish-dir> <output-dir> <asset-prefix>}"
VERSION="${2:?usage: package-arch.sh <x86_64|aarch64> <version> <publish-dir> <output-dir> <asset-prefix>}"
PUBLISH="${3:?usage: package-arch.sh <x86_64|aarch64> <version> <publish-dir> <output-dir> <asset-prefix>}"
OUT="${4:?usage: package-arch.sh <x86_64|aarch64> <version> <publish-dir> <output-dir> <asset-prefix>}"
ASSET_PREFIX="${5:?usage: package-arch.sh <x86_64|aarch64> <version> <publish-dir> <output-dir> <asset-prefix>}"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PKGNAME=pcl-ce
PKGVER="${VERSION}-1"

fail() { echo "[arch-package] $*" >&2; exit 1; }

[ -x "$PUBLISH/PCL.Launcher" ] || fail "missing publish output: $PUBLISH/PCL.Launcher"
command -v zstd >/dev/null 2>&1 || fail "zstd not found; install zstd first"
command -v tar >/dev/null 2>&1 || fail "tar not found"

STAGE="$(mktemp -d "${TMPDIR:-/tmp}/pcl-arch.XXXXXX")"
trap 'rm -rf "$STAGE"' EXIT

mkdir -p "$STAGE/opt/pcl-ce"          "$STAGE/usr/bin"          "$STAGE/usr/share/applications"          "$STAGE/usr/share/icons/hicolor/256x256/apps"          "$STAGE/usr/share/licenses/$PKGNAME"

cp -a "$PUBLISH"/. "$STAGE/opt/pcl-ce/"
find "$STAGE/opt/pcl-ce" -type f -exec chmod 0644 {} +
chmod 0755 "$STAGE/opt/pcl-ce/PCL.Launcher"
printf 'arch\n' > "$STAGE/opt/pcl-ce/.package-kind"

install -m0755 "$ROOT/scripts/linux/pcl-ce-wrapper" "$STAGE/usr/bin/pcl-ce"
install -m0644 "$ROOT/scripts/linux/pcl-community-edition.desktop"   "$STAGE/usr/share/applications/pcl-community-edition.desktop"
install -m0644 "$ROOT/PCL.Launcher/Assets/Images/icon.png"   "$STAGE/usr/share/icons/hicolor/256x256/apps/pcl-community-edition.png"
install -m0644 "$ROOT/LICENSE" "$STAGE/usr/share/licenses/$PKGNAME/LICENSE"
install -m0644 "$ROOT/PCL.Launcher.Core/LICENCE"   "$STAGE/usr/share/licenses/$PKGNAME/LICENCE.PCL.Launcher.Core"

cat > "$STAGE/.INSTALL" <<'INSTALL'
post_install() {
  if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q 2>/dev/null || true
  fi
  if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor 2>/dev/null || true
  fi
}

post_upgrade() {
  post_install
}
INSTALL

SIZE="$(du -sb "$STAGE" | cut -f1)"
BUILDDATE="$(date +%s)"
cat > "$STAGE/.PKGINFO" <<PKGINFO
pkgname = $PKGNAME
pkgbase = $PKGNAME
pkgver = $PKGVER
pkgdesc = PCL Community Edition Minecraft launcher
url = https://github.com/PCL-Community/PCL-CE
builddate = $BUILDDATE
packager = PCL Community <pcl-community@users.noreply.github.com>
size = $SIZE
arch = $ARCH
license = MIT
optdepend = dotnet-runtime: 运行启动器所需的 .NET 运行时
depend = glibc
depend = libx11
depend = libice
depend = libsm
depend = fontconfig
depend = freetype2
depend = mesa
depend = alsa-lib
depend = libpulse
PKGINFO

PKG_FILE="$OUT/${ASSET_PREFIX}_${ARCH}.pkg.tar.zst"
rm -f "$PKG_FILE"
(
  cd "$STAGE"
  tar --zstd --owner=0 --group=0 --numeric-owner -cf "$PKG_FILE" .PKGINFO .INSTALL opt usr
)
tar --zstd -tf "$PKG_FILE" >/dev/null || fail "created package is not readable"
echo "[arch-package] created: $PKG_FILE"

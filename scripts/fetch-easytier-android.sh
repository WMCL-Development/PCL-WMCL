#!/usr/bin/env bash
# 从 WMCL 的 EasyTier fork release 下载 Android 产物到 NativeLibs。
# 二进制不再提交进仓库；CI 与本地构建前执行本脚本即可。
#
# 用法：scripts/fetch-easytier-android.sh [arm64|x86_64|all]
#   默认 all；只构建单一 ABI 时传对应值即可，另一个 ABI 的 zip 不会下载。
#
# 下载策略：
#   1. 优先 GitHub 直连；
#   2. 连接失败、超时，或速度低于 100 KB/s 时自动换镜像；
#   3. 镜像依次为 ghfast.top / gh-proxy.com / ghproxy.net；
#   4. 目标目录里两个 .so 都已存在时跳过下载（CI 缓存命中时直接复用）。
#
# 可用环境变量覆盖：
#   PCL_EASYTIER_REPO    fork 仓库，默认 wenxiangL521/EasyTier
#   PCL_EASYTIER_TAG     release tag，默认 wmcl-easytier-v2.6.4
#   PCL_EASYTIER_MIRRORS 额外镜像前缀，空格分隔
#   PCL_EASYTIER_ABI     同第一个参数
#   PCL_EASYTIER_FORCE=1 忽略已有文件，强制重新下载
set -euo pipefail

REPO="${PCL_EASYTIER_REPO:-wenxiangL521/EasyTier}"
TAG="${PCL_EASYTIER_TAG:-wmcl-easytier-v2.6.4}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST_ARM64="$ROOT/PCL.Launcher.Android/NativeLibs/arm64-v8a"
DEST_X86_64="$ROOT/PCL.Launcher.Android/NativeLibs/x86_64"
GITHUB_BASE="https://github.com/$REPO/releases/download/$TAG"

# 100 KB/s 阈值 + 5 秒测速窗口；curl 的 --speed-limit/--speed-time 正好对应。
MIN_SPEED=102400
SPEED_TIME=5
CONNECT_TIMEOUT=10
MAX_TIME=600

MIRRORS=(
    "https://ghfast.top"
    "https://gh-proxy.com"
    "https://ghproxy.net"
)
if [ -n "${PCL_EASYTIER_MIRRORS:-}" ]; then
    # shellcheck disable=SC2206
    MIRRORS=($PCL_EASYTIER_MIRRORS)
fi

TARGET="${1:-${PCL_EASYTIER_ABI:-all}}"
case "$TARGET" in
    arm64|arm64-v8a|aarch64) WANT_ARM64=1; WANT_X86_64=0 ;;
    x86_64|amd64|x64) WANT_ARM64=0; WANT_X86_64=1 ;;
    all|universal) WANT_ARM64=1; WANT_X86_64=1 ;;
    *)
        echo "未知 ABI：$TARGET（可选 arm64 / x86_64 / all）" >&2
        exit 2
        ;;
esac

is_complete() {
    local dir="$1"
    [ -s "$dir/libeasytier-cli.so" ] && [ -s "$dir/libeasytier_ffi.so" ]
}

mkdir -p "$DEST_ARM64" "$DEST_X86_64"

download_arm64=$WANT_ARM64
download_x86_64=$WANT_X86_64
if [ "${PCL_EASYTIER_FORCE:-0}" != "1" ]; then
    if [ "$download_arm64" = 1 ] && is_complete "$DEST_ARM64"; then download_arm64=0; fi
    if [ "$download_x86_64" = 1 ] && is_complete "$DEST_X86_64"; then download_x86_64=0; fi
fi

if [ "$download_arm64" = 0 ] && [ "$download_x86_64" = 0 ]; then
    echo "==> EasyTier Android 产物已存在，跳过下载"
    ls -la "$DEST_ARM64" "$DEST_X86_64"
    md5sum "$DEST_ARM64"/*.so "$DEST_X86_64"/*.so 2>/dev/null || true
    exit 0
fi

fetch_with_curl() {
    local url="$1" out="$2"
    curl -fL --retry 1 --retry-delay 1 \
        --connect-timeout "$CONNECT_TIMEOUT" --max-time "$MAX_TIME" \
        --speed-limit "$MIN_SPEED" --speed-time "$SPEED_TIME" \
        -o "$out" "$url"
}

fetch_with_python() {
    local url="$1" out="$2"
    python3 - "$url" "$out" "$MIN_SPEED" "$SPEED_TIME" "$CONNECT_TIMEOUT" <<'PY'
import sys, time, urllib.request
url, out, min_speed, speed_time, connect_timeout = sys.argv[1], sys.argv[2], int(sys.argv[3]), float(sys.argv[4]), float(sys.argv[5])
req = urllib.request.Request(url, headers={'User-Agent': 'wmcl-build'})
start = time.monotonic()
try:
    with urllib.request.urlopen(req, timeout=connect_timeout) as r, open(out, 'wb') as f:
        total = 0
        while True:
            chunk = r.read(65536)
            if not chunk:
                break
            f.write(chunk)
            total += len(chunk)
            elapsed = time.monotonic() - start
            if elapsed >= speed_time and total / elapsed < min_speed:
                raise TimeoutError(f"速度过低：{total/1024/elapsed:.0f} KB/s")
except Exception as e:
    try:
        import os; os.remove(out)
    except OSError:
        pass
    print(f"下载失败：{url}（{e}）", file=sys.stderr)
    sys.exit(1)
PY
}

fetch_asset() {
    local name="$1" out="$2"
    local -a candidates=("$GITHUB_BASE/$name")
    for mirror in "${MIRRORS[@]}"; do
        candidates+=("$mirror/$GITHUB_BASE/$name")
    done

    local url
    for url in "${candidates[@]}"; do
        echo "==> 尝试下载：$url"
        if command -v curl >/dev/null 2>&1; then
            if fetch_with_curl "$url" "$out"; then
                echo "    OK：$url"
                return 0
            fi
        else
            if fetch_with_python "$url" "$out"; then
                echo "    OK：$url"
                return 0
            fi
        fi
        echo "    失败或速度低于 100 KB/s，换下一个源"
        rm -f "$out"
    done
    echo "所有下载源均失败：$name" >&2
    return 1
}

echo "==> 下载 EasyTier Android 产物：$REPO@$TAG"

fetch_zip() {
    local name="$1" out="$2"
    local -a candidates=("$GITHUB_BASE/$name")
    for mirror in "${MIRRORS[@]}"; do
        candidates+=("$mirror/$GITHUB_BASE/$name")
    done
    local url
    for url in "${candidates[@]}"; do
        echo "==> 尝试下载：$url"
        if command -v curl >/dev/null 2>&1; then
            if fetch_with_curl "$url" "$out"; then echo "    OK：$url"; return 0; fi
        else
            if fetch_with_python "$url" "$out"; then echo "    OK：$url"; return 0; fi
        fi
        echo "    失败或速度低于 100 KB/s，换下一个源"
        rm -f "$out"
    done
    echo "所有下载源均失败：$name" >&2
    return 1
}

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

if [ "$download_arm64" = 1 ]; then
    fetch_zip "easytier-android-arm64-v2.6.4.zip" "$TMP/arm64.zip"
    unzip -q "$TMP/arm64.zip" -d "$TMP/arm64"
    cp -f "$TMP/arm64/easytier-android-arm64/easytier-cli" "$DEST_ARM64/libeasytier-cli.so"
    cp -f "$TMP/arm64/easytier-android-arm64/libeasytier_ffi.so" "$DEST_ARM64/libeasytier_ffi.so"
    chmod 755 "$DEST_ARM64"/*.so
fi

if [ "$download_x86_64" = 1 ]; then
    fetch_zip "easytier-android-x86_64-v2.6.4.zip" "$TMP/x86_64.zip"
    unzip -q "$TMP/x86_64.zip" -d "$TMP/x86_64"
    cp -f "$TMP/x86_64/easytier-android-x86_64/easytier-cli" "$DEST_X86_64/libeasytier-cli.so"
    cp -f "$TMP/x86_64/easytier-android-x86_64/libeasytier_ffi.so" "$DEST_X86_64/libeasytier_ffi.so"
    chmod 755 "$DEST_X86_64"/*.so
fi

echo "==> 已就绪："
ls -la "$DEST_ARM64" "$DEST_X86_64"
md5sum "$DEST_ARM64"/*.so "$DEST_X86_64"/*.so 2>/dev/null || true

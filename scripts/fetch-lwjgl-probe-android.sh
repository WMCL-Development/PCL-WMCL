#!/usr/bin/env bash
# 为 WMCL 的 Android/Java 首帧探针准备“仅下载、仅核验”的固定输入素材。
#
# 本脚本只做三件事：
#   1. 从 FCL 固定 commit 的 raw 地址下载 lwjgl-3.3.3-natives-release.aar，
#      校验整个 AAR 的 SHA-256 后，只抽取 arm64-v8a/liblwjgl.so；
#   2. 从 Maven Central 下载 org.lwjgl:lwjgl:3.3.3 的 Java classes JAR，
#      校验 SHA-256 后固定为 lwjgl-3.3.3.jar；
#   3. 把 AAR 内 assets/licenses/ 的 13 份许可证原文一并取出到 licenses/。
#
# 重要边界（请勿在集成时误读）：
#   - AAR 的 SHA-256/URL 固定只代表“下载到的字节固定”，不证明该 AAR 由
#     FCL commit caaab583... 的 CI 构建，也不建立源码与产物的密码学绑定；
#   - liblwjgl.so 是 arm64-v8a / Android bionic / ELF64 AArch64，
#     不能在 x86_64 glibc HotSpot 上直接 System.load；
#   - lwjgl-3.3.3.jar 只含 Java classes（另有 META-INF 下的 .so.sha1 占位），
#     仅用于 HotSpot 侧签名/反射探针，绝不可作为 ART 的 AndroidJavaLibrary，
#     也不含任何真正的 native 库；
#   - 默认不向仓库写二进制。输出目录必须显式给出；若位于仓库内，脚本会拒绝，
#     除非显式传 --allow-repo-output（仍会打印警告）。
#
# 用法：
#   scripts/fetch-lwjgl-probe-android.sh --out /tmp/wmcl-lwjgl-probe
#   scripts/fetch-lwjgl-probe-android.sh --out /tmp/wmcl-lwjgl-probe --verify-only
#   scripts/fetch-lwjgl-probe-android.sh --out /tmp/wmcl-lwjgl-probe --force
#
# 输出：
#   <out>/liblwjgl.so
#   <out>/lwjgl-3.3.3.jar
#   <out>/licenses/*.txt
#   <out>/SHA256SUMS
#   <out>/PROVENANCE.txt
#
# 下载均走匿名 HTTPS，不使用 token、不访问 GitHub API、不写 git 对象。
# 重复执行时：已有输出且 SHA256SUMS 全部通过 -> 直接复用，不联网。
# 已存在目标但哈希不符 -> 报错，需显式 --force；不会静默覆盖。
set -euo pipefail
export LC_ALL=C
umask 022

readonly FCL_COMMIT="caaab583772e2c62b54b5c1e7694fbed3fa18323"
readonly FCL_AAR_PATH="FCL/libs/lwjgl-3.3.3-natives-release.aar"
readonly FCL_AAR_URL="https://raw.githubusercontent.com/FCL-Team/FoldCraftLauncher/$FCL_COMMIT/$FCL_AAR_PATH"
readonly FCL_AAR_SHA256="ec26f2008e5c4b73605d5f401e8f3df8f4e0eb74d2e66fbba7d349a1f530552f"
readonly FCL_AAR_SIZE=16269860

readonly AAR_SO_MEMBER="assets/app_runtime/lwjgl/3.3.3/natives/arm64-v8a/liblwjgl.so"
readonly LWJGL_SO_SHA256="70e8642abcd11a368c0b4cbbeae31e33f2e031553ba3a05f9375fc6b92c8a40f"
readonly LWJGL_SO_SIZE=511504

readonly MAVEN_JAR_URL="https://repo1.maven.org/maven2/org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3.jar"
readonly MAVEN_JAR_SHA256="dc9c7b2d48e8396d68895f8902ffa01e46253de44dfe927533ff09457ebfeec4"
readonly MAVEN_JAR_SHA1="29589b5f87ed335a6c7e7ee6a5775f81f97ecb84"
readonly MAVEN_JAR_SIZE=785029

# 与 AAR 内 assets/licenses/ 完全一致的 13 份文本；只取这些名字，不做通配。
# 用空格分隔的固定列表，便于在 bash /python 之间按固定顺序传递。
readonly LICENSE_LIST="blendish_license.txt freetype_license.txt glfw_license.txt khronos_license.txt libffi_license.txt liburing_license.txt lwjgl_license.txt nanosvg_license.txt nanovg_license.txt openal_soft_license.txt shaderc_license.txt tinyfd_license.txt vma_license.txt"

ROOT="$(cd "$(dirname "$BASH_SOURCE")/.." && pwd -P)"
OUT=""
FORCE=0
VERIFY_ONLY=0
ALLOW_REPO_OUTPUT=0
STAGE=""

usage() {
    cat <<'EOF'
用法:
  scripts/fetch-lwjgl-probe-android.sh --out <目录> [--force] [--verify-only] [--allow-repo-output]

选项:
  --out <目录>              必填。输出目录；建议使用 /tmp 下的显式临时目录。
                           目录不存在时会创建。
  --force                   允许覆盖已存在但哈希不符的目标文件，并强制重新下载。
  --verify-only             完全不联网、不写入；只校验 <目录> 内现有输出及 SHA256SUMS。
  --allow-repo-output       允许把输出目录放在本仓库内（默认拒绝；仍会警告）。
  -h, --help                显示本帮助。

说明:
  - 无默认输出目录，避免误把 16 MB AAR/511 KB .so 写进仓库。
  - 输出固定为 liblwjgl.so、lwjgl-3.3.3.jar、licenses/ 及校验清单。
  - 重复执行且校验通过时直接复用；--force 才会重新下载/覆盖。
  - JAR 仅用于 HotSpot 探针，不能作为 Android 的 AndroidJavaLibrary。
EOF
}

die() {
    printf 'ERROR: %s\n' "$*" >&2
    exit 1
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --out)
            [ "$#" -ge 2 ] || { usage >&2; exit 2; }
            OUT="$2"
            shift 2
            ;;
        --force|-f)
            FORCE=1
            shift
            ;;
        --verify-only)
            VERIFY_ONLY=1
            shift
            ;;
        --allow-repo-output)
            ALLOW_REPO_OUTPUT=1
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            printf 'ERROR: 未知参数：%s\n' "$1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

[ -n "$OUT" ] || { usage >&2; exit 2; }
command -v python3 >/dev/null 2>&1 || die "需要 python3（用于 SHA-256、ZIP 解包和清单校验）"

if [ "$VERIFY_ONLY" = 1 ]; then
    [ -d "$OUT" ] || die "--verify-only 需要已存在的输出目录：$OUT"
else
    if [ -e "$OUT" ] && [ ! -d "$OUT" ]; then
        die "输出路径已存在且不是目录：$OUT"
    fi
fi

# 先解析真实路径再决定是否创建，避免“仓库内输出”在拒绝前已被建目录。
OUT_REAL="$(python3 - "$OUT" <<'PY'
import os
import sys
print(os.path.realpath(os.path.abspath(sys.argv[1])))
PY
)"

[ "$OUT_REAL" != "/" ] || die "拒绝把根目录作为输出目录"

case "$OUT_REAL/" in
    "$ROOT"/*)
        if [ "$ALLOW_REPO_OUTPUT" != 1 ]; then
            die "输出目录位于本仓库内：$OUT_REAL
默认不向仓库写二进制/第三方文本。请改到 /tmp 等仓库外目录；
若确实要写入仓库，需显式加 --allow-repo-output。"
        fi
        printf 'WARNING: 输出目录位于仓库内（已通过 --allow-repo-output）：%s\n' "$OUT_REAL" >&2
        ;;
esac

if [ "$VERIFY_ONLY" != 1 ]; then
    mkdir -p "$OUT_REAL"
fi

if [ -L "$OUT_REAL/licenses" ]; then
    die "拒绝操作符号链接目录：$OUT_REAL/licenses"
fi
if [ -e "$OUT_REAL/licenses" ] && [ ! -d "$OUT_REAL/licenses" ]; then
    die "licenses 已存在但不是目录：$OUT_REAL/licenses"
fi
for name in PROVENANCE.txt SHA256SUMS; do
    if [ -e "$OUT_REAL/$name" ] && [ ! -f "$OUT_REAL/$name" ]; then
        die "$name 已存在但不是普通文件：$OUT_REAL/$name"
    fi
done

cleanup() {
    if [ -n "$STAGE" ] && [ -d "$STAGE" ]; then
        rm -rf "$STAGE"
    fi
}
trap cleanup EXIT

sha256_file() {
    python3 - "$1" <<'PY'
import hashlib
import sys

h = hashlib.sha256()
with open(sys.argv[1], "rb") as f:
    for chunk in iter(lambda: f.read(1024 * 1024), b""):
        h.update(chunk)
print(h.hexdigest())
PY
}

file_size() {
    python3 - "$1" <<'PY'
import os
import sys
print(os.path.getsize(sys.argv[1]))
PY
}

check_pinned_file() {
    local path="$1" expected_sha="$2" expected_size="$3" label="$4"
    [ -f "$path" ] || die "$label 不存在：$path"
    local actual_size actual_sha
    actual_size="$(file_size "$path")"
    [ "$actual_size" = "$expected_size" ] ||
        die "$label 大小不符：期望 $expected_size，实际 $actual_size（$path）"
    actual_sha="$(sha256_file "$path")"
    [ "$actual_sha" = "$expected_sha" ] ||
        die "$label SHA-256 不符：期望 $expected_sha，实际 $actual_sha（$path）"
    printf '    OK %-58s %10s B  sha256=%s\n' "$label" "$actual_size" "$actual_sha"
}

check_existing_target() {
    local path="$1" expected_sha="$2" label="$3"
    [ -e "$path" ] || return 0
    [ -L "$path" ] && die "拒绝覆盖符号链接：$path"
    [ -f "$path" ] || die "目标已存在但不是普通文件：$path"
    local actual_sha
    actual_sha="$(sha256_file "$path")"
    if [ "$actual_sha" != "$expected_sha" ] && [ "$FORCE" != 1 ]; then
        die "$label 已存在且不是本次固定产物：$path
  期望 SHA-256: $expected_sha
  实际 SHA-256: $actual_sha
确认后加 --force 覆盖。"
    fi
}

fetch() {
    local url="$1" dest="$2" max_size="$3"
    echo "==> 下载 $url"
    rm -f "$dest"
    if command -v curl >/dev/null 2>&1; then
        curl -fL --silent --show-error \
            --proto '=https' --proto-redir '=https' \
            --tlsv1.2 --retry 3 --retry-delay 2 \
            --connect-timeout 20 --max-time 300 \
            --max-filesize "$max_size" \
            --user-agent 'wmcl-lwjgl-probe-fetch/1.0' \
            -o "$dest" "$url"
    else
        python3 - "$url" "$dest" "$max_size" <<'PY'
import os
import ssl
import sys
import urllib.request

url, dest, max_size = sys.argv[1], sys.argv[2], int(sys.argv[3])
req = urllib.request.Request(url, headers={"User-Agent": "wmcl-lwjgl-probe-fetch/1.0"})
ctx = ssl.create_default_context()
try:
    with urllib.request.urlopen(req, timeout=60, context=ctx) as r, open(dest, "wb") as f:
        final_url = r.geturl()
        if not final_url.startswith("https://"):
            raise RuntimeError(f"重定向到非 HTTPS 地址：{final_url}")
        total = 0
        while True:
            chunk = r.read(1024 * 1024)
            if not chunk:
                break
            total += len(chunk)
            if total > max_size:
                raise RuntimeError(f"响应超过上限 {max_size} 字节")
            f.write(chunk)
except Exception as exc:
    try:
        os.remove(dest)
    except OSError:
        pass
    print(f"下载失败：{url}（{exc}）", file=sys.stderr)
    sys.exit(1)
PY
    fi
    [ -s "$dest" ] || die "下载结果为空：$url"
}

verify_outputs() {
    python3 - "$1" $LICENSE_LIST <<'PY'
import hashlib
import os
import sys
import zipfile

out = sys.argv[1]
license_names = sys.argv[2:]

ARTIFACTS = {
    "liblwjgl.so": (
        "70e8642abcd11a368c0b4cbbeae31e33f2e031553ba3a05f9375fc6b92c8a40f",
        511504,
    ),
    "lwjgl-3.3.3.jar": (
        "dc9c7b2d48e8396d68895f8902ffa01e46253de44dfe927533ff09457ebfeec4",
        785029,
    ),
}
JAR_CLASS_MAJOR = 52


def fail(msg):
    print(f"ERROR: {msg}", file=sys.stderr)
    sys.exit(1)


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def require_regular(path, label):
    if not os.path.exists(path):
        fail(f"{label} 缺失：{path}")
    if os.path.islink(path):
        fail(f"{label} 是符号链接，拒绝校验：{path}")
    if not os.path.isfile(path):
        fail(f"{label} 不是普通文件：{path}")


for rel, (expected_sha, expected_size) in ARTIFACTS.items():
    path = os.path.join(out, rel)
    require_regular(path, rel)
    actual_size = os.path.getsize(path)
    if actual_size != expected_size:
        fail(f"{rel} 大小不符：期望 {expected_size}，实际 {actual_size}")
    actual_sha = sha256(path)
    if actual_sha != expected_sha:
        fail(f"{rel} SHA-256 不符：期望 {expected_sha}，实际 {actual_sha}")

# JAR 结构：必须是 Java classes-only；不得含真正 native 库。
jar_path = os.path.join(out, "lwjgl-3.3.3.jar")
try:
    with zipfile.ZipFile(jar_path) as zf:
        bad = zf.testzip()
        if bad:
            fail(f"JAR CRC 损坏：{bad}")
        names = {info.filename for info in zf.infolist()}
        native_suffixes = (".so", ".dll", ".dylib", ".jnilib")
        natives = sorted(n for n in names if n.endswith(native_suffixes))
        if natives:
            fail(f"JAR 意外包含 native 库：{natives[:5]}")
        if "org/lwjgl/system/JNI.class" not in names:
            fail("JAR 缺少 org/lwjgl/system/JNI.class")
        class_bytes = zf.read("org/lwjgl/system/JNI.class")
        if class_bytes[:4] != b"\xca\xfe\xba\xbe":
            fail("JNI.class 不是合法 class 文件")
        major = int.from_bytes(class_bytes[6:8], "big")
        if major != JAR_CLASS_MAJOR:
            fail(f"JNI.class major version 为 {major}，期望 {JAR_CLASS_MAJOR}")
except zipfile.BadZipFile as exc:
    fail(f"JAR 不是合法 ZIP：{exc}")

# liblwjgl.so 的 ELF 头：64-bit little-endian AArch64 ET_DYN。
so_path = os.path.join(out, "liblwjgl.so")
with open(so_path, "rb") as f:
    hdr = f.read(64)
if hdr[:4] != b"\x7fELF":
    fail("liblwjgl.so 不是 ELF")
if hdr[4] != 2 or hdr[5] != 1:
    fail("liblwjgl.so 不是 ELF64 little-endian")
e_type = int.from_bytes(hdr[16:18], "little")
e_machine = int.from_bytes(hdr[18:20], "little")
if e_type != 3:
    fail(f"liblwjgl.so 不是 ET_DYN（实际 e_type={e_type}）")
if e_machine != 183:
    fail(f"liblwjgl.so 不是 AArch64（实际 e_machine={e_machine}）")

manifest_path = os.path.join(out, "SHA256SUMS")
require_regular(manifest_path, "SHA256SUMS")
expected_paths = set(ARTIFACTS) | {f"licenses/{name}" for name in license_names}
manifest = {}
with open(manifest_path, "r", encoding="utf-8", newline="\n") as f:
    for lineno, line in enumerate(f, 1):
        line = line.rstrip("\n")
        if not line:
            continue
        parts = line.split(None, 1)
        if len(parts) != 2:
            fail(f"SHA256SUMS 第 {lineno} 行格式错误")
        digest, rel = parts[0], parts[1].lstrip("*")
        if len(digest) != 64 or any(c not in "0123456789abcdef" for c in digest):
            fail(f"SHA256SUMS 第 {lineno} 行不是 SHA-256")
        if os.path.isabs(rel) or ".." in rel.split("/"):
            fail(f"SHA256SUMS 第 {lineno} 行包含不安全路径：{rel}")
        if rel in manifest:
            fail(f"SHA256SUMS 重复条目：{rel}")
        manifest[rel] = digest

if set(manifest) != expected_paths:
    missing = sorted(expected_paths - set(manifest))
    extra = sorted(set(manifest) - expected_paths)
    fail(f"SHA256SUMS 条目不匹配；缺失={missing}，多余={extra}")

for rel in sorted(expected_paths):
    path = os.path.join(out, rel)
    require_regular(path, rel)
    actual = sha256(path)
    if actual != manifest[rel]:
        fail(f"{rel} 与 SHA256SUMS 不符：清单 {manifest[rel]}，实际 {actual}")
    if os.path.getsize(path) == 0:
        fail(f"{rel} 为空文件")

print(f"verify: OK  {len(expected_paths)} 个文件；JAR major=52 且无 native；liblwjgl.so 为 ELF64 AArch64 ET_DYN")
PY
}

extract_aar() {
    python3 - "$1" "$2" "$LWJGL_SO_SHA256" "$LWJGL_SO_SIZE" $LICENSE_LIST <<'PY'
import hashlib
import os
import sys
import zipfile

aar_path, stage, expected_so_sha, expected_so_size = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4])
license_names = sys.argv[5:]
member = "assets/app_runtime/lwjgl/3.3.3/natives/arm64-v8a/liblwjgl.so"


def fail(msg):
    print(f"ERROR: {msg}", file=sys.stderr)
    sys.exit(1)


try:
    zf = zipfile.ZipFile(aar_path)
except zipfile.BadZipFile as exc:
    fail(f"AAR 不是合法 ZIP：{exc}")

with zf:
    bad = zf.testzip()
    if bad:
        fail(f"AAR CRC 损坏：{bad}")
    names = set(zf.namelist())
    if member not in names:
        fail(f"AAR 缺少成员：{member}")
    data = zf.read(member)
    if len(data) != expected_so_size:
        fail(f"liblwjgl.so 大小不符：期望 {expected_so_size}，实际 {len(data)}")
    actual_sha = hashlib.sha256(data).hexdigest()
    if actual_sha != expected_so_sha:
        fail(f"liblwjgl.so SHA-256 不符：期望 {expected_so_sha}，实际 {actual_sha}")

    so_path = os.path.join(stage, "liblwjgl.so")
    with open(so_path, "wb") as f:
        f.write(data)
    os.chmod(so_path, 0o755)

    lic_dir = os.path.join(stage, "licenses")
    os.makedirs(lic_dir, exist_ok=True)
    for name in license_names:
        member_name = f"assets/licenses/{name}"
        if member_name not in names:
            fail(f"AAR 缺少许可证：{member_name}")
        payload = zf.read(member_name)
        if not payload:
            fail(f"许可证为空：{member_name}")
        path = os.path.join(lic_dir, name)
        with open(path, "wb") as f:
            f.write(payload)
        os.chmod(path, 0o644)

print(f"extract: OK  {member} -> liblwjgl.so；licenses/ {len(license_names)} 份")
PY
}

write_provenance() {
    local generated_at
    generated_at="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
    {
        cat <<EOF
WMCL LWJGL Android probe assets
Generated by: scripts/fetch-lwjgl-probe-android.sh
Generated at: $generated_at
Output dir:   $OUT_REAL

!!! 这不是“源码/CI 产物已绑定”的声明 !!!
下面固定的是下载 URL 与字节哈希；FCL AAR 内没有 commit/build 元数据，
因此不能证明它由 $FCL_COMMIT 的 CI 构建，也不能证明它等于该 commit 的任何产物。
本文件仅用于探针输入的字节级复核。

FCL AAR（liblwjgl.so 来源）
  URL:        $FCL_AAR_URL
  commit pin: $FCL_COMMIT（仅固定 raw URL 路径）
  size:       $FCL_AAR_SIZE B
  SHA-256:    $FCL_AAR_SHA256
  extracted:  $AAR_SO_MEMBER
  member size: $LWJGL_SO_SIZE B
  member SHA-256: $LWJGL_SO_SHA256
  ABI:        arm64-v8a / Android bionic / ELF64 AArch64 / ET_DYN

Maven Central Java classes JAR（仅 HotSpot 探针）
  Coordinates: org.lwjgl:lwjgl:3.3.3
  URL:         $MAVEN_JAR_URL
  size:        $MAVEN_JAR_SIZE B
  SHA-256:     $MAVEN_JAR_SHA256
  SHA-1:       $MAVEN_JAR_SHA1（与 Maven Central 的 .sha1 一致）
  role:        Java classes only（class major 52）；JAR 内没有真正的 native 库。
  warning:     不能作为 ART 的 AndroidJavaLibrary；它依赖的 liblwjgl.so 必须是
               对应宿主/目标平台的原生库。AAR 中的 arm64-v8a bionic 库不能
               在 x86_64 glibc HotSpot 上加载。

Licenses
  licenses/ 下 13 份文本逐字取自 AAR 的 assets/licenses/；其中包含 LWJGL、
  libffi 及 AAR 声明的第三方组件文本。是否与实际分发组合一一对应，需在集成
  阶段另行核对；本脚本不删除、不改写这些文本。

Files
  liblwjgl.so
  lwjgl-3.3.3.jar
  SHA256SUMS（对上述产物与许可证逐文件校验）
EOF
        printf '  licenses/%s\n' $LICENSE_LIST
    } > "$STAGE/PROVENANCE.txt"
    chmod 644 "$STAGE/PROVENANCE.txt"
}

write_manifest() {
    python3 - "$STAGE" $LICENSE_LIST <<'PY'
import hashlib
import os
import sys

stage = sys.argv[1]
license_names = sys.argv[2:]
paths = ["liblwjgl.so", "lwjgl-3.3.3.jar"] + [f"licenses/{name}" for name in license_names]
lines = []
for rel in paths:
    path = os.path.join(stage, rel)
    if not os.path.isfile(path):
        raise SystemExit(f"ERROR: 清单生成时缺少 {rel}")
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    lines.append(f"{h.hexdigest()}  {rel}\n")

with open(os.path.join(stage, "SHA256SUMS"), "w", encoding="utf-8", newline="\n") as f:
    f.writelines(lines)
os.chmod(os.path.join(stage, "SHA256SUMS"), 0o644)
print(f"manifest: OK  {len(paths)} 个条目")
PY
}

commit_outputs() {
    mkdir -p "$OUT_REAL/licenses"
    # SHA256SUMS 是提交标记：先移除旧标记，避免中途失败后旧清单指向新旧混合文件。
    rm -f "$OUT_REAL/SHA256SUMS"
    local name
    for name in $LICENSE_LIST; do
        mv -f "$STAGE/licenses/$name" "$OUT_REAL/licenses/$name"
    done
    mv -f "$STAGE/lwjgl-3.3.3.jar" "$OUT_REAL/lwjgl-3.3.3.jar"
    mv -f "$STAGE/liblwjgl.so" "$OUT_REAL/liblwjgl.so"
    mv -f "$STAGE/PROVENANCE.txt" "$OUT_REAL/PROVENANCE.txt"
    # 清单最后落盘：看到它才代表本次提交完成。
    mv -f "$STAGE/SHA256SUMS" "$OUT_REAL/SHA256SUMS"
}

print_artifacts() {
    local out="$1"
    printf '==> 输出目录：%s\n' "$out"
    printf '    %-28s %10s B  sha256=%s\n' "liblwjgl.so" "$(file_size "$out/liblwjgl.so")" "$(sha256_file "$out/liblwjgl.so")"
    printf '    %-28s %10s B  sha256=%s\n' "lwjgl-3.3.3.jar" "$(file_size "$out/lwjgl-3.3.3.jar")" "$(sha256_file "$out/lwjgl-3.3.3.jar")"
    printf '    %-28s %s 份\n' "licenses/" "$(find "$out/licenses" -maxdepth 1 -type f -name '*_license.txt' | wc -l | tr -d ' ')"
    printf '    %s\n' "SHA256SUMS / PROVENANCE.txt"
}

if [ "$VERIFY_ONLY" = 1 ]; then
    verify_outputs "$OUT_REAL"
    echo "==> --verify-only 通过：未联网、未写入"
    print_artifacts "$OUT_REAL"
    exit 0
fi

if [ "$FORCE" != 1 ] &&
   [ -f "$OUT_REAL/liblwjgl.so" ] &&
   [ -f "$OUT_REAL/lwjgl-3.3.3.jar" ] &&
   [ -f "$OUT_REAL/SHA256SUMS" ] &&
   [ -f "$OUT_REAL/PROVENANCE.txt" ] &&
   [ -d "$OUT_REAL/licenses" ]; then
    if verify_outputs "$OUT_REAL"; then
        echo "==> 已有输出通过固定哈希校验，跳过下载（--force 可强制重建）"
        print_artifacts "$OUT_REAL"
        exit 0
    fi
    die "已有输出校验失败；为免覆盖，未自动重建。确认后加 --force。"
fi

# 下载前先检查最终目标：存在但哈希不符时必须显式 --force。
check_existing_target "$OUT_REAL/liblwjgl.so" "$LWJGL_SO_SHA256" "liblwjgl.so"
check_existing_target "$OUT_REAL/lwjgl-3.3.3.jar" "$MAVEN_JAR_SHA256" "lwjgl-3.3.3.jar"

STAGE="$(mktemp -d "$OUT_REAL/.wmcl-lwjgl-stage.XXXXXX")"

# AAR 上限留 1 MiB 余量，但下载后仍按精确大小 + SHA-256 校验。
fetch "$FCL_AAR_URL" "$STAGE/lwjgl-3.3.3-natives-release.aar" "$((FCL_AAR_SIZE + 1048576))"
check_pinned_file "$STAGE/lwjgl-3.3.3-natives-release.aar" "$FCL_AAR_SHA256" "$FCL_AAR_SIZE" "FCL AAR"

fetch "$MAVEN_JAR_URL" "$STAGE/lwjgl-3.3.3.jar" "$((MAVEN_JAR_SIZE + 1048576))"
check_pinned_file "$STAGE/lwjgl-3.3.3.jar" "$MAVEN_JAR_SHA256" "$MAVEN_JAR_SIZE" "Maven classes JAR"

extract_aar "$STAGE/lwjgl-3.3.3-natives-release.aar" "$STAGE"

# 许可证也属于输出；若已有内容与本次固定 AAR 不一致，必须 --force。
for name in $LICENSE_LIST; do
    dest="$OUT_REAL/licenses/$name"
    if [ -e "$dest" ]; then
        [ -L "$dest" ] && die "拒绝覆盖符号链接：$dest"
        [ -f "$dest" ] || die "许可证目标不是普通文件：$dest"
        if ! cmp -s "$STAGE/licenses/$name" "$dest"; then
            [ "$FORCE" = 1 ] ||
                die "许可证已存在且与固定 AAR 不一致：$dest（确认后加 --force）"
        fi
    fi
done

write_provenance
write_manifest

# 先在暂存目录做一次完整校验，再提交。
verify_outputs "$STAGE"

commit_outputs
# 提交后再校验最终目录，防止跨文件系统/复制异常。
verify_outputs "$OUT_REAL"

echo "==> 完成（未建立源码与产物的绑定；liblwjgl.so 仅 arm64-v8a Android）"
print_artifacts "$OUT_REAL"
echo "==> 提醒：lwjgl-3.3.3.jar 只用于 HotSpot 探针，不是 ART 的 AndroidJavaLibrary。"

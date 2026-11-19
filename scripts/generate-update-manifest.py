#!/usr/bin/env python3
"""生成应用内更新所需的 wmcl-update.json。"""
import hashlib
import json
import os
import pathlib
import sys


def main() -> int:
    if len(sys.argv) < 4:
        print("usage: generate-update-manifest.py <configuration> <version> <release-dir>",
              file=sys.stderr)
        return 2

    config, version = sys.argv[1], sys.argv[2]
    release_dir = pathlib.Path(sys.argv[3])
    channel = sys.argv[4] if len(sys.argv) > 4 else config
    suffix = sys.argv[5] if len(sys.argv) > 5 else ""
    tag = sys.argv[6] if len(sys.argv) > 6 else f"v{version}"
    repo = os.environ.get("GITHUB_REPOSITORY", "wenxiangL521/PCL-WMCL")

    metadata = json.loads(
        pathlib.Path("PCL.Launcher/Resources/metadata.json").read_text(encoding="utf-8"))
    base = metadata["version"]["base"]
    code = int(metadata["version"]["code"])

    candidates = {
        "windows_x64": f"PCL2_CE_{config}_windows_x64.zip",
        "windows_ARM64": f"PCL2_CE_{config}_windows_ARM64.zip",
        "linux_x64": f"PCL2_CE_{config}_linux_x64.AppImage",
        "linux_ARM64": f"PCL2_CE_{config}_linux_ARM64.AppImage",
        "macos_x64": f"PCL2_CE_{config}_macos_x64.zip",
        "macos_ARM64": f"PCL2_CE_{config}_macos_ARM64.zip",
        "android_arm64": f"PCL2_CE_{config}_android_arm64.apk",
        "android_x86_64": f"PCL2_CE_{config}_android_x86_64.apk",
    }

    assets: dict[str, dict[str, str]] = {}
    for key, name in candidates.items():
        path = release_dir / name
        if not path.is_file():
            continue
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        assets[key] = {
            "url": f"https://github.com/{repo}/releases/download/{tag}/{name}",
            "sha256": digest,
        }

    display_version = base + (f"-{suffix}" if suffix else "")
    manifest = {
        "version": display_version,
        "code": code,
        "channel": channel,
        "notes": f"WMCL {display_version}（{channel}）的更新说明请查看 GitHub Release 页面。",
        "assets": assets,
    }
    output = release_dir / "wmcl-update.json"
    output.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"[update-manifest] {output} assets={sorted(assets)}")
    return 0 if assets else 1


if __name__ == "__main__":
    raise SystemExit(main())

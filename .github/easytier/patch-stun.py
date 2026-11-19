#!/usr/bin/env python3
"""给 EasyTier 的 TCP STUN 列表补充国内可达服务器（NAT 检测需要至少 2 个 TCP 服务器响应）。"""
import pathlib
import sys

root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "EasyTier")
path = root / "easytier" / "src" / "common" / "stun.rs"
text = path.read_text(encoding="utf-8")

marker = "const DEFAULT_TCP_STUN_SERVERS: &[&str] = &["
if "stun.hitv.com" not in text:
    if marker not in text:
        raise SystemExit(f"未找到 TCP STUN 列表：{path}")
    text = text.replace(marker, marker + '\n    "stun.hitv.com",', 1)
    path.write_text(text, encoding="utf-8")
    print("已补充 stun.hitv.com")
else:
    print("TCP STUN 列表已包含 stun.hitv.com")

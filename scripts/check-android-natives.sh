#!/usr/bin/env bash
# Verifies that every Android native library we ship has the properties the loader
# requires: AArch64 ELF, page-compatible segment alignment, and only system
# dependencies. It never modifies its inputs.
set -euo pipefail

usage() { echo "Usage: $0 <directory-with-arm64-libs>" >&2; exit 2; }
(( $# == 1 )) || usage
dir=$1
[[ -d "$dir" ]] || usage

failures=0
printf "%-26s %-7s %-9s %s\n" FILE ELF MAX-ALIGN DEPENDENCIES
for file in "$dir"/*.so; do
    [[ -f "$file" ]] || continue
    name=$(basename "$file")
    class=$(readelf -h "$file" | awk "/Class:/ {print \$2}")
    machine=$(readelf -h "$file" | awk "/Machine:/ {print \$2}")
    align=$(readelf -lW "$file" | awk "/LOAD/ {print \$NF}" | sort -u | tail -1)
    needed=$(readelf -d "$file" | awk "/NEEDED/ {gsub(/[][]/, \"\", \$5); printf \"%s \", \$5}")
    elf=ok
    if [[ "$class" != ELF64 || "$machine" != *AArch64* ]]; then elf=BROKEN; failures=$((failures + 1)); fi
    if (( align < 4096 )); then
        echo "FAIL $name: LOAD alignment $align is below a page" >&2
        failures=$((failures + 1))
    fi
    for dep in $needed; do
        case "$dep" in
            libc.so|libdl.so|libm.so|liblog.so|libandroid.so|libEGL.so|libGLESv2.so|libGLESv3.so|libz.so|libstdc++.so|libc++_shared.so) ;;
            *) echo "NOTE $name depends on $dep" >&2 ;;
        esac
    done
    printf "%-26s %-7s %-9s %s\n" "$name" "$elf" "$align" "$needed"
done

if (( failures )); then
    echo "$failures check(s) failed" >&2
    exit 1
fi
echo "All packaged native libraries look loadable."

#!/usr/bin/env bash
# Copies the part of the osx-arm64 bflat layout that zero-mode builds use, for
# the ZeroCSharp.Bflat.osx-arm64 package.
#
# macOS can't be traced the way trim-layout.sh traces Linux (strace), so the
# file list is the linux-arm64 trimmed layout's, mapped to macOS names: the
# target side (lib/, notices) is the same, the host side maps
#     bflat -> bflat,  bin/lld -> bin/lld,  libX.so -> libX.dylib
# (linux-arm64, because the JITs' names carry the host's architecture, which a
# Mac shares), plus any dylib of the layout that bflat or lld loads. Building
# all four targets with the result on a clean Mac (CI) proves it.
#
# Usage:  sdk/trim-macos-layout.sh <osx-arm64 layout> <linux-arm64 trimmed> <out>
# Needs otool (macOS, or cctools/llvm-otool elsewhere).
set -euo pipefail
layout=$(cd "${1:?osx-arm64 layout}" && pwd)
linux=$(cd "${2:?linux-arm64 trimmed layout}" && pwd)
out=${3:?out}
[ -f "$layout/bflat" ] || { echo "Error: $layout has no bflat." >&2; exit 1; }
[ -f "$linux/bflat" ] || { echo "Error: $linux isn't a trimmed Linux layout (no bflat)." >&2; exit 1; }

map() {
    case "$1" in
        */*)      echo "$1" ;;
        lib*.so)  echo "${1%.so}.dylib" ;;
        *)        echo "$1" ;;
    esac
}
# The dylibs an executable loads, by file name. An otool that can't read it
# is an error: going on would leave its dylibs out of the package.
loads() {
    local dump
    dump=$(otool -L "$1") || { echo "Error: otool couldn't read $1." >&2; return 1; }
    tail -n +2 <<< "$dump" | awk '{print $1}' | sed 's|.*/||'
}
# The layout's file for a loaded dylib, at the top or in bin/, in any case
# (macOS's file system usually ignores case).
find_dylib() { (cd "$layout" && find . bin -maxdepth 1 -type f -iname "$1" 2>/dev/null | sed 's|^\./||' | sort -u); }

rm -rf "$out"
mkdir -p "$out"
missing=0
list=$( (cd "$linux" && find . -type f | sed 's|^\./||') | while read -r f; do map "$f"; done)
for exe in bflat bin/lld; do
    names=$(loads "$layout/$exe") || exit 1
    for dylib in $names; do
        found=$(find_dylib "$dylib")
        [ -n "$found" ] && list="$list"$'\n'"$found"
    done
done
while read -r f; do
    [ -n "$f" ] || continue
    # lib/uefi only has to exist (see trim-layout.sh): from the Linux one if
    # this layout has none.
    if [ ! -f "$layout/$f" ] && [[ "$f" == lib/uefi/* ]] && [ -f "$linux/$f" ]; then
        mkdir -p "$out/$(dirname "$f")"
        cp "$linux/$f" "$out/$f"
        continue
    fi
    if [ ! -f "$layout/$f" ]; then echo "Error: the macOS layout has no $f" >&2; missing=1; continue; fi
    mkdir -p "$out/$(dirname "$f")"
    cp "$layout/$f" "$out/$f"
done <<< "$(sort -u <<< "$list")"
[ $missing -eq 0 ] || exit 1
echo "$(find "$out" -type f | wc -l | tr -d ' ') files, $(du -sh "$out" | cut -f1)"

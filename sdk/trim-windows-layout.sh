#!/usr/bin/env bash
# Copies the part of the windows-x64 bflat layout that zero-mode builds use,
# for the ZeroCSharp.Bflat.win-x64 package.
#
# Windows can't be traced the way trim-layout.sh traces Linux (strace), so the
# file list is the linux-x64 trimmed layout's, mapped to Windows names: the
# target side (lib/, notices) is the same, and the host side maps
#     bflat -> bflat.exe,  bin/lld -> bin/lld.exe,  libX.so -> X.dll
# plus any DLL of the layout that bflat.exe or lld.exe imports. Building all
# three targets with the result on a clean Windows machine (CI) proves it.
#
# Usage:  sdk/trim-windows-layout.sh <windows-x64 layout> <linux-x64 trimmed> <out>
# Needs an objdump that reads PE files (binutils, or llvm-objdump on macOS).
set -euo pipefail
layout=$(cd "${1:?windows layout}" && pwd)
linux=$(cd "${2:?linux-x64 trimmed layout}" && pwd)
out=${3:?out}
[ -f "$layout/bflat.exe" ] || { echo "Error: $layout has no bflat.exe." >&2; exit 1; }
[ -f "$linux/bflat" ] || { echo "Error: $linux isn't a trimmed Linux layout (no bflat)." >&2; exit 1; }

map() {
    case "$1" in
        bflat)            echo bflat.exe ;;
        bin/lld)          echo bin/lld.exe ;;
        */*)              echo "$1" ;;
        lib*.so)          local f=${1#lib}; echo "${f%.so}.dll" ;;
        *)                echo "$1" ;;
    esac
}
imports() { objdump -p "$1" 2>/dev/null | sed -n 's/.*DLL Name: *//p' | tr -d '\r' || true; }

rm -rf "$out"
mkdir -p "$out"
missing=0
list=$( (cd "$linux" && find . -type f | sed 's|^\./||') | while read -r f; do map "$f"; done)
for exe in bflat.exe bin/lld.exe; do
    for dll in $(imports "$layout/$exe"); do
        [ -f "$layout/$dll" ] && list="$list"$'\n'"$dll"
        [ -f "$layout/bin/$dll" ] && list="$list"$'\n'"bin/$dll"
    done
done
while read -r f; do
    [ -n "$f" ] || continue
    if [ ! -f "$layout/$f" ]; then echo "Error: the Windows layout has no $f" >&2; missing=1; continue; fi
    mkdir -p "$out/$(dirname "$f")"
    cp "$layout/$f" "$out/$f"
done <<< "$(sort -u <<< "$list")"
[ $missing -eq 0 ] || exit 1
echo "$(find "$out" -type f | wc -l | tr -d ' ') files, $(du -sh "$out" | cut -f1)"

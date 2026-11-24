#!/usr/bin/env bash
# Puts SDL's own Windows x64 files in native/win-x64/ (not in git): the import
# library programs link with, the DLL they load, and SDL's license. The
# package carries them, so Windows programs need no SDL installed; Linux and
# macOS use the system's SDL.
#
# Usage:  sdk/ZeroCSharp.SDL3/fetch-windows-sdl.sh
# Needs:  curl, unzip (or PowerShell on Windows)
set -euo pipefail
cd "$(dirname "$0")"
version=3.4.20
sha256=1ec3009655ee37b1f73e52b3b120acc5afd8abaf9532f3303b1bf1dfe91b5969
url=https://github.com/libsdl-org/SDL/releases/download/release-$version/SDL3-devel-$version-VC.zip
out=native/win-x64

if [ -f "$out/VERSION" ] && [ "$(cat "$out/VERSION")" = "$version" ]; then
    echo "SDL $version for Windows is already in $out"
    exit 0
fi
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
curl -fsSL "$url" -o "$tmp/sdl.zip"
actual=$( (sha256sum "$tmp/sdl.zip" 2> /dev/null || shasum -a 256 "$tmp/sdl.zip") | cut -d' ' -f1)
if [ "$actual" != "$sha256" ]; then
    echo "Error: $url has SHA-256 $actual, not $sha256" >&2
    exit 1
fi
mkdir -p "$tmp/x"
if command -v unzip > /dev/null; then
    unzip -q "$tmp/sdl.zip" -d "$tmp/x"
else
    powershell.exe -NoProfile -Command "Expand-Archive -LiteralPath '$(cygpath -w "$tmp/sdl.zip")' -DestinationPath '$(cygpath -w "$tmp/x")'"
fi
mkdir -p "$out"
src=$tmp/x/SDL3-$version
cp "$src/lib/x64/SDL3.lib" "$src/lib/x64/SDL3.dll" "$out/"
cp "$src/LICENSE.txt" "$out/SDL-LICENSE.txt"
echo "$version" > "$out/VERSION"
echo "SDL $version for Windows -> $out"

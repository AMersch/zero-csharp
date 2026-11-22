#!/usr/bin/env bash
# Makes the trimmed bflat layout for one host in <trimmed root>/<rid>, from the
# full layout in <layouts>, unless the one there is still current.
#
# Current means: the stamp in <trimmed root>/<rid>.source matches a checksum
# of every full-layout file the trimmed one copied, plus the trimming script
# (and for win-x64, the linux-x64 file list it is mapped from). Checksums,
# not times: copies and checkouts don't keep times in order.
#
# Without the full layout (CI downloads the trimmed one), the trimmed one is
# used as it is.
#
# Usage:  sdk/trim-host.sh <rid> <layouts> <trimmed root>
#         rid: linux-x64, linux-arm64 or win-x64
set -euo pipefail
cd "$(dirname "$0")/.."
rid=${1:?rid}
layouts=${2:?layouts}
root=${3:?trimmed root}
trimmed=$root/$rid

if [ "$rid" = win-x64 ]; then
    layout=$layouts/windows-x64
    exe=bflat.exe
    script=sdk/trim-windows-layout.sh
else
    layout=$layouts/linux-glibc-${rid#linux-}
    exe=bflat
    script=sdk/trim-layout.sh
fi

if [ ! -f "$layout/$exe" ]; then
    [ -f "$trimmed/$exe" ] && exit 0
    echo "Error: no layout in $layout and no trimmed one in $trimmed." >&2
    exit 1
fi
if [ "$rid" = win-x64 ] && [ ! -f "$root/linux-x64/bflat" ]; then
    echo "Error: win-x64 is trimmed from the linux-x64 trimmed layout, and $root/linux-x64 has none. Trim linux-x64 first." >&2
    exit 1
fi

files() { (cd "$1" && find . -type f | sed 's|^\./||' | LC_ALL=C sort); }
stamp() {
    cat "$script"
    [ "$rid" = win-x64 ] && files "$root/linux-x64"
    files "$trimmed" | while read -r f; do echo "$f"; cat "$layout/$f"; done
}

if [ -f "$trimmed/$exe" ] && [ "$(cat "$root/$rid.source" 2>/dev/null)" = "$(stamp | cksum)" ]; then
    exit 0
fi
echo "[trim]  $layout -> $trimmed"
if [ "$rid" = win-x64 ]; then
    "$script" "$layout" "$root/linux-x64" "$trimmed"
else
    "$script" "$layout" "$trimmed"
fi
stamp | cksum > "$root/$rid.source"

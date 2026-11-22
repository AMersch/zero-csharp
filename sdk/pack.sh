#!/usr/bin/env bash
# Packs the Zero C# packages into sdk/feed/:
#   ZeroCSharp.Sdk               the MSBuild SDK and its payload (./build.sh --sdk-payload)
#   ZeroCSharp.Bflat.<rid>       bflat for each host, trimmed (sdk/trim-layout.sh, and for
#                                Windows sdk/trim-windows-layout.sh from the linux-x64 one)
#   ZeroCSharp.Templates         dotnet new zero and zerolib (when it exists)
#
# Usage:  sdk/pack.sh
# Env:    ZERO_HOSTS     the hosts to pack bflat for (default "linux-x64 linux-arm64 win-x64";
#                        win-x64 needs sdk/trimmed/linux-x64)
#         ZERO_LAYOUTS   where the full layouts are (default ../bflat/layouts):
#                        linux-glibc-x64, linux-glibc-arm64, windows-x64
#         BFLAT_FORK, BFLAT_COMMIT   where the fork is checked out, or its commit
#                        (the bflat packages name it; see build.sh --sdk-payload)
#         ZERO_PAYLOAD_PREBUILT=1   use sdk/ZeroCSharp.Sdk/payload as it is (CI builds it
#                        in another job, like sdk/trimmed/)
# Trimmed layouts are kept in sdk/trimmed/<rid> and redone only when the
# layout's bflat or lld, or the trimming script, differ from the ones they were
# made from (by checksum: copies and checkouts don't keep times in order).
set -euo pipefail
cd "$(dirname "$0")/.."
hosts=${ZERO_HOSTS:-linux-x64 linux-arm64 win-x64}
layouts=${ZERO_LAYOUTS:-../bflat/layouts}
payload=sdk/ZeroCSharp.Sdk/payload

mkdir -p sdk/feed
rm -f sdk/feed/ZeroCSharp.*.nupkg
[ -n "${ZERO_PAYLOAD_PREBUILT:-}" ] || ./build.sh --sdk-payload "$payload"
bflat_commit=$(sed -n 's/^bflat //p' "$payload/VERSIONS.txt")
# The bflat packages are AGPL: they must name the exact source they were built from.
if ! [[ "$bflat_commit" =~ ^[0-9a-f]{40}$ ]]; then
    echo "Error: the bflat fork's commit is '$bflat_commit'. Set BFLAT_FORK to its checkout, or BFLAT_COMMIT." >&2
    exit 1
fi

for rid in $hosts; do
    trimmed=sdk/trimmed/$rid
    if [ "$rid" = win-x64 ]; then
        layout=$layouts/windows-x64
        exe=bflat.exe
        trim() { sdk/trim-windows-layout.sh "$layout" sdk/trimmed/linux-x64 "$trimmed"; }
        # The Windows list is the linux-x64 one, mapped: it changes with it.
        source_files() { cat "$layout/bflat.exe" "$layout/bin/lld.exe" sdk/trim-windows-layout.sh; (cd sdk/trimmed/linux-x64 && find . -type f | sort); }
    else
        layout=$layouts/linux-glibc-${rid#linux-}
        exe=bflat
        trim() { sdk/trim-layout.sh "$layout" "$trimmed"; }
        source_files() { cat "$layout/bflat" "$layout/bin/lld" sdk/trim-layout.sh; }
    fi
    # Without the full layout (CI downloads the trimmed one), use what's there.
    if [ -f "$layout/$exe" ]; then
        source_sum=$(source_files | cksum)
        if [ ! -f "$trimmed/$exe" ] || [ "$(cat "sdk/trimmed/$rid.source" 2>/dev/null)" != "$source_sum" ]; then
            echo "[trim]  $layout -> $trimmed"
            trim
            echo "$source_sum" > "sdk/trimmed/$rid.source"
        fi
    elif [ ! -f "$trimmed/$exe" ]; then
        echo "Error: no layout in $layout and no trimmed one in $trimmed." >&2
        exit 1
    fi
    dotnet pack sdk/ZeroCSharp.Bflat/ZeroCSharp.Bflat.csproj -o sdk/feed -v:q -nologo \
        -p:HostRid="$rid" -p:LayoutDir="$PWD/$trimmed" -p:BflatCommit="$bflat_commit"
done

dotnet pack sdk/ZeroCSharp.Sdk/ZeroCSharp.Sdk.csproj -o sdk/feed -v:q -nologo
if [ -f sdk/ZeroCSharp.Templates/ZeroCSharp.Templates.csproj ]; then
    dotnet pack sdk/ZeroCSharp.Templates/ZeroCSharp.Templates.csproj -o sdk/feed -v:q -nologo
fi
ls -la sdk/feed

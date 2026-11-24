#!/usr/bin/env bash
# Builds what F# programs compile against, from zerocore's IL:
#
#   <out>/System.Runtime.dll            zerocore, under the name fsc looks for
#   <out>/System.Runtime.Extensions.dll shim/: the BCL types fsc needs that
#                                       zerocore doesn't have
#   <out>/FSharp.Core.dll               core/: a small FSharp.Core
#   <out>/FSharp.Printf.dll             printf/: printf, sprintf, failwithf
#
# Usage:  fsharp/build-core.sh <System.Private.CoreLib.dll> <BflatLib.dll> <out dir>
#         (printf writes through lib/'s Out)
# Env:    DOTNET_10_SDK   the SDK whose compilers to use (default: .NET SDK
#                         10.0.402, which must be installed; see below)
#
# Only the .NET SDK is needed (its C# and F# compilers), not bflat.
#
# Why each piece is the way it is:
# - fsc takes framework types (Tuple, IComparable, ...) only from assemblies
#   whose names are on its list of system assemblies, so the shim is named
#   System.Runtime.Extensions.
# - With --targetprofile:netcore fsc finds its primary assembly by the file
#   name System.Runtime.dll, and the IL it writes refers to that assembly's
#   real name, System.Private.CoreLib: bflat then finds zerocore.
# - zerocore's metadata version is empty; fsc would copy it and write ECMA
#   v1.x metadata tables, which bflat misreads. --metadataversion sets it.
# - --reflectionfree: records and unions get no sprintf-based ToString.
# - The F# compiler is exactly the one in SDK 10.0.402. Later ones (10.0.404,
#   11.0.100) fail on core.fs: "'Microsoft.FSharp.Core' ... did not contain
#   ... 'Operators'". They look up Operators (box, for the equality code they
#   generate for Choice and the other unions) before ops.fs defines it; the real
#   FSharp.Core declares it ahead in its .fsi signatures, which this one lacks.
#   Programs compile against the result with any F# compiler. Install it with
#   dotnet-install.sh --version 10.0.402 (or actions/setup-dotnet), beside others.
set -euo pipefail
usage="usage: build-core.sh <System.Private.CoreLib.dll> <BflatLib.dll> <out dir>"
core=${1:?$usage}
lib=${2:?$usage}
out=${3:?$usage}
here=$(cd "$(dirname "$0")" && pwd)

fsc_sdk=10.0.402
if [ -z "${DOTNET_10_SDK:-}" ]; then
    DOTNET_10_SDK=$(dotnet --list-sdks | awk -v want=$fsc_sdk '$1 == want {d = $2; gsub(/[][]/, "", d); print d "/" $1}')
fi
if [ -z "$DOTNET_10_SDK" ] || [ ! -f "$DOTNET_10_SDK/FSharp/fsc.dll" ]; then
    echo "Error: the .NET SDK $fsc_sdk isn't installed (its F# compiler builds FSharp.Core; later ones fail," >&2
    echo "see fsharp/build-core.sh): dotnet-install.sh --version $fsc_sdk, or set DOTNET_10_SDK" >&2
    exit 1
fi

mkdir -p "$out"
cp "$core" "$out/System.Runtime.dll"

echo "[csc]   fsharp/shim/ -> $out/System.Runtime.Extensions.dll"
dotnet "$DOTNET_10_SDK/Roslyn/bincore/csc.dll" -nologo -noconfig -nostdlib -deterministic -optimize+ \
    -t:library -r:"$out/System.Runtime.dll" -out:"$out/System.Runtime.Extensions.dll" "$here"/shim/*.cs

echo "[fsc]   fsharp/core/ -> $out/FSharp.Core.dll"
dotnet "$DOTNET_10_SDK/FSharp/fsc.dll" --nologo --noframework --targetprofile:netcore --compiling-fslib \
    --reflectionfree --optimize+ --deterministic+ --metadataversion:v4.0.30319 --nowarn:75,3370 \
    --target:library -r:"$out/System.Runtime.dll" -r:"$out/System.Runtime.Extensions.dll" \
    --out:"$out/FSharp.Core.dll" \
    "$here/core/prelude.fs" "$here/core/attrs.fs" "$here/core/core.fs" "$here/core/ops.fs" "$here/core/local.fs" \
    "$here/core/seqcore.fs" "$here/core/seq.fs" "$here/core/collections.fs" "$here/core/map.fs" "$here/core/set.fs" "$here/core/string.fs" \
    "$here/core/nativeptr.fs" "$here/core/printf.fs"

# printf and the others: a library of their own (see printf/Printf.fs), as
# any F# library, against FSharp.Core and lib/ (for Out).
echo "[fsc]   fsharp/printf/ -> $out/FSharp.Printf.dll"
dotnet "$DOTNET_10_SDK/FSharp/fsc.dll" --nologo --noframework --targetprofile:netcore \
    --reflectionfree --optimize+ --deterministic+ --metadataversion:v4.0.30319 --nowarn:75,3370 \
    --target:library -r:"$out/System.Runtime.dll" -r:"$out/System.Runtime.Extensions.dll" -r:"$out/FSharp.Core.dll" -r:"$lib" \
    --out:"$out/FSharp.Printf.dll" "$here/printf/Printf.fs"

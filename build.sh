#!/usr/bin/env bash
# Builds the --stdlib:zero samples with the bflat fork, which compiles C# 15
# with the updated memory safety rules in one step.
#
# On macOS bflat runs in a Linux container (see docker/Dockerfile); the
# binaries in ./bin are Linux binaries, so use ./run.sh to run them.
#
# Usage:  ./build.sh [Sample ...]     default: all samples
# Env:    BFLAT_DIR, BFLAT_FORK, BFLAT_ARCH (see env.sh)
set -euo pipefail
cd "$(dirname "$0")"
source ./env.sh

ALL_SAMPLES=(Tiny Minimal Arena Collections VirtualTest Simd Kernels)

# Language version and memory safety rules for every sample.
CSHARP_FLAGS=(--langversion preview --compiler-feature updated-memory-safety-rules)

# lib/ is compiled once into an IL assembly that the samples reference with -r,
# the way a C program links a library, except the assembly's metadata replaces
# the header files. It holds IL, not native code: each sample's bflat build
# compiles the parts it uses, for its own -m flags, into a single binary.
# -O matters: unlike bflat build, build-il doesn't optimize unless asked.
# Don't name it ZeroLib: zero mode's core library is an assembly called zerolib,
# and assembly names are case-insensitive.
LIB=bin/BflatLib.dll

check_sample() {
    case " ${ALL_SAMPLES[*]} " in
        *" $1 "*) ;;
        *) echo "Unknown sample: $1 (known: ${ALL_SAMPLES[*]})" >&2; return 1 ;;
    esac
}

bflat_flags() {
    case "$1" in
        Tiny) echo -Os --no-pie ;;
        # Zero mode has no runtime CPU detection, so SIMD extensions must be part of
        # the baseline. x64: AVX2/FMA (Haswell, 2013, or newer). arm64 always has
        # NEON; dotprod (SDOT, for int8) needs ARMv8.2+, e.g. Apple M1 or Cortex-A76.
        Simd | Kernels)
            if [ "$BFLAT_ARCH" = x64 ]; then echo -m x86-64-v3; else echo -m dotprod; fi ;;
    esac
}

# arm64 links with 64 KB segment alignment by default, which pads even Tiny
# (~5 KB of code) to ~200 KB. 4 KB pages give the same sizes as x64, but the
# binaries then won't load on 16 KB/64 KB-page kernels (Asahi Linux, some
# servers); set ARM64_MAX_PAGE_SIZE=65536 for those.
ARM64_MAX_PAGE_SIZE=${ARM64_MAX_PAGE_SIZE:-4096}
LINK_FLAGS=()
if [ "$BFLAT_ARCH" = arm64 ]; then
    LINK_FLAGS=(--ldflags "-z max-page-size=$ARM64_MAX_PAGE_SIZE")
fi

check_bflat() {
    if [ ! -x "$BFLAT_DIR/bflat" ]; then
        echo "Error: no bflat at $BFLAT_DIR" >&2
        echo "       Build the fork's layouts (README, \"Getting bflat\") or set BFLAT_DIR." >&2
        exit 1
    fi
}

build_native() {
    local name
    check_bflat
    mkdir -p bin
    echo "[bflat] lib/*.cs -> $LIB"
    "$BFLAT_DIR/bflat" build-il lib/*.cs -O --stdlib:zero "${CSHARP_FLAGS[@]}" --target Shared -o "$LIB"
    for name in "$@"; do
        echo "[bflat] $name -> bin/$name"
        # shellcheck disable=SC2046
        "$BFLAT_DIR/bflat" build "samples/$name.cs" -r "$LIB" --stdlib:zero "${CSHARP_FLAGS[@]}" \
            --arch "$BFLAT_ARCH" $(bflat_flags "$name") ${LINK_FLAGS[@]+"${LINK_FLAGS[@]}"} -o "bin/$name"
        if [ "$name" = Tiny ]; then
            if [ "$BFLAT_ARCH" = "$HOST_ARCH" ]; then
                strip "bin/$name"
            else
                echo "  (not stripped: strip can't handle $BFLAT_ARCH binaries on $HOST_ARCH)"
            fi
        fi
    done
}

# Internal entry point used inside the container.
if [ "${1:-}" = "--native" ]; then
    shift
    build_native "$@"
    exit 0
fi

if [ $# -eq 0 ]; then
    set -- "${ALL_SAMPLES[@]}"
fi
for name in "$@"; do
    check_sample "$name"
done

if [ "$(uname -s)" = Darwin ]; then
    check_bflat
    ensure_image
    docker run --rm --platform "$DOCKER_PLATFORM" \
        -v "$PWD:/w" -v "$(cd "$BFLAT_DIR" && pwd):/bflat:ro" -w /w \
        -e BFLAT_DIR=/bflat -e BFLAT_ARCH="$BFLAT_ARCH" -e ARM64_MAX_PAGE_SIZE="$ARM64_MAX_PAGE_SIZE" \
        "$IMAGE" ./build.sh --native "$@"
else
    build_native "$@"
fi

echo
for name in "$@"; do
    printf '%-12s %8d bytes\n' "$name" "$(wc -c < "bin/$name")"
done

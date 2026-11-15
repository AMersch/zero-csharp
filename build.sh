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

ALL_SAMPLES=(Tiny Minimal Arena Collections VirtualTest Simd)

# Language version and memory safety rules for every sample.
CSHARP_FLAGS=(--langversion preview --compiler-feature updated-memory-safety-rules)

sources() {
    case "$1" in
        Tiny)        echo samples/Tiny.cs ;;
        Minimal)     echo samples/Minimal.cs ;;
        Arena)       echo samples/Arena.cs lib/Arena.cs lib/Out.cs ;;
        Collections) echo samples/Collections.cs lib/Arena.cs lib/List.cs lib/Dict.cs lib/Out.cs ;;
        VirtualTest) echo samples/VirtualTest.cs lib/Out.cs ;;
        Simd)        echo samples/Simd.cs lib/Arena.cs lib/Out.cs ;;
        *) echo "Unknown sample: $1 (known: ${ALL_SAMPLES[*]})" >&2; return 1 ;;
    esac
}

bflat_flags() {
    case "$1" in
        Tiny) echo -Os --no-pie ;;
        # Zero mode has no runtime CPU detection, so AVX2/FMA must be part of the
        # baseline (Haswell, 2013, or newer). arm64 always has NEON.
        Simd) if [ "$BFLAT_ARCH" = x64 ]; then echo -m x86-64-v3; fi ;;
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
    for name in "$@"; do
        echo "[bflat] $name -> bin/$name"
        # shellcheck disable=SC2046
        "$BFLAT_DIR/bflat" build $(sources "$name") --stdlib:zero "${CSHARP_FLAGS[@]}" \
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
    sources "$name" >/dev/null
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

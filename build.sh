#!/usr/bin/env bash
# Builds the --stdlib:zero samples with the bflat fork, which compiles C# 15
# with the updated memory safety rules in one step.
#
# On macOS bflat runs in a Linux container (see docker/Dockerfile); the
# binaries in ./bin are Linux binaries, so use ./run.sh to run them.
#
# With --wasm, builds WebAssembly modules (bin/wasm/*.wasm, WASI) instead, with
# NativeAOT-LLVM and the WASI SDK (./setup.sh --wasm downloads them).
#
# Usage:  ./build.sh [--wasm] [Sample ...]     default: all samples
# Env:    BFLAT_DIR, BFLAT_FORK, BFLAT_ARCH, WASM_TOOLS (see env.sh)
set -euo pipefail
cd "$(dirname "$0")"
source ./env.sh

ALL_SAMPLES=(Tiny Minimal Arena Collections VirtualTest Simd Kernels Strings)

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

build_lib() {
    mkdir -p bin
    echo "[bflat] lib/*.cs -> $LIB"
    "$BFLAT_DIR/bflat" build-il lib/*.cs -O --stdlib:zero "${CSHARP_FLAGS[@]}" --target Shared -o "$LIB"
}

build_native() {
    local name
    check_bflat
    build_lib
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

WASI_ZEROLIB=$BFLAT_DIR/lib/wasi/wasm/zerolib.dll

check_wasm() {
    if [ ! -x "$WASM_ILC" ] || [ ! -x "$WASI_SDK/bin/clang" ]; then
        echo "Error: no WebAssembly toolchain in $WASM_TOOLS. Run ./setup.sh --wasm" >&2
        exit 1
    fi
    if [ ! -f "$WASI_ZEROLIB" ]; then
        echo "Error: no $WASI_ZEROLIB (the bflat layout predates WASI support; rebuild the fork)" >&2
        exit 1
    fi
}

# Source -> IL (bflat, as for the native build) -> LLVM bitcode (NativeAOT-LLVM,
# against zerolib's WASI build) -> WASM objects (clang) -> one module, linked with
# wasm/runtime.c (the zero-mode runtime) and WASI's C library.
build_wasm() {
    local name obj bc
    check_bflat
    check_wasm
    build_lib
    for name in "$@"; do
        echo "[wasm]  $name -> bin/wasm/$name.wasm"
        obj=bin/wasm/obj/$name
        rm -rf "$obj"
        mkdir -p "$obj"

        "$BFLAT_DIR/bflat" build-il "samples/$name.cs" -r "$LIB" -O --stdlib:zero "${CSHARP_FLAGS[@]}" -o "$obj/$name.dll"

        # --directpinvoke binds [DllImport("libc")] and ("libm.so.6") at link time,
        # to WASI's C library.
        if ! "$WASM_ILC" "$obj/$name.dll" -r "$LIB" -r "$WASI_ZEROLIB" --systemmodule:zerolib \
                --targetos:wasi --targetarch:wasm -O --wasm-global-base:1024 \
                --codegenopt:Target=wasm32-unknown-wasip2 --codegenopt:LlvmExceptionHandlingModel=emulated \
                --directpinvoke:libc --directpinvoke:libm.so.6 -o:"$obj/$name.bc" > "$obj/ilc.log" 2>&1; then
            cat "$obj/ilc.log" >&2
            exit 1
        fi

        # $name.N.bc are bitcode; $name.bc itself is already a WASM object (the
        # module's data), and $name.external.o needs no compiling.
        for bc in "$obj/$name".*.bc; do
            "$WASI_SDK/bin/clang" "$bc" -c -o "${bc%.bc}.o" -target wasm32-unknown-wasip2 -O2 \
                -mllvm -combiner-global-alias-analysis=false -mllvm -disable-lsr
        done
        cp "$obj/$name.bc" "$obj/$name.data.o"

        # wasip1, not wasip2: a WASI 0.2 exit only says success or failure, and the
        # samples report failures in their exit code. --no-stack-first: WASI SDK 34
        # puts the stack first by default, which clashes with the 1 KB global base.
        "$WASI_SDK/bin/clang" -target wasm32-wasip1 -Os "$obj"/*.o wasm/runtime.c -lm -o "bin/wasm/$name.wasm" \
            -Wl,--no-stack-first -Wl,--global-base=1024 -Wl,-z,stack-size=1048576 \
            -Wl,--strip-all -Wl,--gc-sections
    done
}

# Internal entry points used inside the container.
if [ "${1:-}" = "--native" ]; then
    shift
    build_native "$@"
    exit 0
fi
if [ "${1:-}" = "--native-wasm" ]; then
    shift
    build_wasm "$@"
    exit 0
fi

WASM=0
if [ "${1:-}" = "--wasm" ]; then
    WASM=1
    shift
fi

if [ $# -eq 0 ]; then
    set -- "${ALL_SAMPLES[@]}"
fi
for name in "$@"; do
    check_sample "$name"
done

if [ "$WASM" = 1 ]; then
    entry=--native-wasm
    output() { echo "bin/wasm/$1.wasm"; }
else
    entry=--native
    output() { echo "bin/$1"; }
fi

if [ "$(uname -s)" = Darwin ]; then
    check_bflat
    ensure_image
    # WASM_TOOLS must be inside this directory to be visible in the container.
    docker run --rm --platform "$DOCKER_PLATFORM" \
        -v "$PWD:/w" -v "$(cd "$BFLAT_DIR" && pwd):/bflat:ro" -w /w \
        -e BFLAT_DIR=/bflat -e BFLAT_ARCH="$BFLAT_ARCH" -e ARM64_MAX_PAGE_SIZE="$ARM64_MAX_PAGE_SIZE" \
        -e WASM_TOOLS="$WASM_TOOLS" \
        "$IMAGE" ./build.sh $entry "$@"
elif [ "$WASM" = 1 ]; then
    build_wasm "$@"
else
    build_native "$@"
fi

echo
for name in "$@"; do
    printf '%-12s %8d bytes\n' "$name" "$(wc -c < "$(output "$name")")"
done

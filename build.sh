#!/usr/bin/env bash
# Builds the samples with the bflat fork, which compiles C# 15 with the updated
# memory safety rules in one step. They run without .NET's runtime: the core
# library is zerocore/ (see zerocore/README.md), compiled from source here.
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

ALL_SAMPLES=(Tiny Minimal Arena Collections VirtualTest Casts Core Simd Kernels Strings)

# Language version and memory safety rules for every sample.
CSHARP_FLAGS=(--langversion preview --compiler-feature updated-memory-safety-rules)

# zerocore/ is the core library: object, string, Span<T>, and the runtime code
# the compiler expects to find (allocation, casts, static fields). bflat only
# accepts a custom core library under the name System.Private.CoreLib.
# --stdlib:none means "no built-in library": every build references this one.
CORE=bin/System.Private.CoreLib.dll
CORE_FLAGS=(--stdlib:none --langversion preview)

# lib/ is compiled once into an IL assembly that the samples reference with -r,
# the way a C program links a library, except the assembly's metadata replaces
# the header files. It holds IL, not native code: each sample's bflat build
# compiles the parts it uses, for its own -m flags, into a single binary.
# -O matters: unlike bflat build, build-il doesn't optimize unless asked.
LIB=bin/BflatLib.dll

# What every native build needs besides the sources:
# --no-reflection   bflat assumes a library named System.Private.CoreLib is
#                   .NET's and would look for its reflection support
# -i libc ...       bind [DllImport("libc")] at link time; binding at run time
#                   needs runtime support zerocore doesn't have
NATIVE_FLAGS=(--no-reflection -i libc -i libm.so.6)

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
# zerocore's few assembly routines (zerocore/native), linked into every binary.
CORE_NATIVE=bin/zerocore-$BFLAT_ARCH.o
LINK_FLAGS=(--ldflags "$CORE_NATIVE")
if [ "$BFLAT_ARCH" = arm64 ]; then
    LINK_FLAGS+=(--ldflags "-z max-page-size=$ARM64_MAX_PAGE_SIZE")
fi

check_bflat() {
    if [ ! -x "$BFLAT_DIR/bflat" ]; then
        echo "Error: no bflat at $BFLAT_DIR" >&2
        echo "       Build the fork's layouts (README, \"Getting bflat\") or set BFLAT_DIR." >&2
        exit 1
    fi
}

# $1: output assembly; the rest: extra flags (-d WASM for the WebAssembly build).
build_core() {
    local out=$1
    shift
    mkdir -p "$(dirname "$out")"
    echo "[bflat] zerocore/ -> $out"
    # shellcheck disable=SC2046
    "$BFLAT_DIR/bflat" build-il $(find zerocore -name '*.cs' | sort) -O "${CORE_FLAGS[@]}" "$@" -o "$out"
}

# Any assembler for the target will do: the cross one from the build image, or
# plain `as` when building on the target's own architecture.
build_core_native() {
    local triple as
    case "$BFLAT_ARCH" in
        x64)   triple=x86_64-linux-gnu ;;
        arm64) triple=aarch64-linux-gnu ;;
    esac
    if command -v "$triple-as" >/dev/null 2>&1; then
        as=$triple-as
    elif [ "$BFLAT_ARCH" = "$HOST_ARCH" ] && command -v as >/dev/null 2>&1; then
        as=as
    else
        echo "Error: no assembler for $BFLAT_ARCH. Install binutils (binutils-$triple to cross-compile)." >&2
        exit 1
    fi
    echo "[as]    zerocore/native/$BFLAT_ARCH.S -> $CORE_NATIVE"
    "$as" "zerocore/native/$BFLAT_ARCH.S" -o "$CORE_NATIVE"
}

build_lib() {
    echo "[bflat] lib/*.cs -> $LIB"
    "$BFLAT_DIR/bflat" build-il lib/*.cs -O --stdlib:none -r "$CORE" "${CSHARP_FLAGS[@]}" --target Shared -o "$LIB"
}

build_native() {
    local name
    check_bflat
    build_core "$CORE"
    build_core_native
    build_lib
    for name in "$@"; do
        echo "[bflat] $name -> bin/$name"
        # shellcheck disable=SC2046
        "$BFLAT_DIR/bflat" build "samples/$name.cs" -r "$LIB" -r "$CORE" --stdlib:none "${CSHARP_FLAGS[@]}" \
            "${NATIVE_FLAGS[@]}" --arch "$BFLAT_ARCH" $(bflat_flags "$name") "${LINK_FLAGS[@]}" -o "bin/$name"
        if [ "$name" = Tiny ]; then
            if [ "$BFLAT_ARCH" = "$HOST_ARCH" ]; then
                strip "bin/$name"
            else
                echo "  (not stripped: strip can't handle $BFLAT_ARCH binaries on $HOST_ARCH)"
            fi
        fi
    done
}

# zerocore's WebAssembly build differs in how it finds static fields (-d WASM).
WASM_CORE=bin/wasm/System.Private.CoreLib.dll

check_wasm() {
    if [ ! -x "$WASM_ILC" ] || [ ! -x "$WASI_SDK/bin/clang" ]; then
        echo "Error: no WebAssembly toolchain in $WASM_TOOLS. Run ./setup.sh --wasm" >&2
        exit 1
    fi
}

# Source -> IL (bflat, as for the native build) -> LLVM bitcode (NativeAOT-LLVM,
# against zerocore's WebAssembly build) -> WASM objects (clang) -> one module,
# linked with wasm/runtime.c (zerocore's native half there) and WASI's C library.
build_wasm() {
    local name obj bc
    check_bflat
    check_wasm
    build_core "$CORE"
    build_core "$WASM_CORE" -d WASM
    build_lib
    for name in "$@"; do
        echo "[wasm]  $name -> bin/wasm/$name.wasm"
        obj=bin/wasm/obj/$name
        rm -rf "$obj"
        mkdir -p "$obj"

        "$BFLAT_DIR/bflat" build-il "samples/$name.cs" -r "$LIB" -r "$CORE" -O --stdlib:none "${CSHARP_FLAGS[@]}" -o "$obj/$name.dll"

        # --directpinvoke binds [DllImport("libc")] and ("libm.so.6") at link time,
        # to WASI's C library.
        if ! "$WASM_ILC" "$obj/$name.dll" -r "$LIB" -r "$WASM_CORE" --reflectiondata:none \
                --generateunmanagedentrypoints:System.Private.CoreLib \
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
        # --fatal-warnings: if wasm/runtime.c's signature for a function no longer
        # matches what NativeAOT-LLVM's code calls, wasm-ld only warns and links a
        # trap; this makes it a build error instead.
        "$WASI_SDK/bin/clang" -target wasm32-wasip1 -Os "$obj"/*.o wasm/runtime.c -lm -o "bin/wasm/$name.wasm" \
            -Wl,--no-stack-first -Wl,--global-base=1024 -Wl,-z,stack-size=1048576 \
            -Wl,--strip-all -Wl,--gc-sections -Wl,--fatal-warnings
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

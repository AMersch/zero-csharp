#!/usr/bin/env bash
# Checks the prerequisites for build.sh and, on macOS, builds the container image.
# With --wasm, also downloads the WebAssembly toolchain (see env.sh) into
# $WASM_TOOLS: about 900 MB unpacked, most of it the WASI SDK.
#
# bflat comes from the fork (OnurGumus/bflat, branch master), which must be
# built first; see README, "Getting bflat".
#
# Usage: ./setup.sh [--wasm]
set -euo pipefail
cd "$(dirname "$0")"
source ./env.sh

ok=1
want_wasm=0
[ "${1:-}" = --wasm ] && want_wasm=1

fetch_wasm_tools() {
    mkdir -p "$WASM_TOOLS"

    if [ ! -x "$WASM_ILC" ]; then
        local id="runtime.$WASM_HOST_NUGET.microsoft.dotnet.ilcompiler.llvm"
        local dir="$WASM_TOOLS/ilc-$NAOT_LLVM_VERSION"
        echo "Downloading NativeAOT-LLVM $NAOT_LLVM_VERSION ($WASM_HOST_NUGET)..."
        curl -fsSL -o "$WASM_TOOLS/ilc.nupkg" \
            "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-experimental/nuget/v3/flat2/$id/$NAOT_LLVM_VERSION/$id.$NAOT_LLVM_VERSION.nupkg"
        rm -rf "$dir"
        mkdir -p "$dir"
        unzip -q "$WASM_TOOLS/ilc.nupkg" -d "$dir"
        rm "$WASM_TOOLS/ilc.nupkg"
        chmod +x "$WASM_ILC"
    fi

    if [ ! -x "$WASI_SDK/bin/clang" ]; then
        echo "Downloading WASI SDK $WASI_SDK_VERSION ($WASI_SDK_ARCH-linux)..."
        curl -fsSL "https://github.com/WebAssembly/wasi-sdk/releases/download/wasi-sdk-${WASI_SDK_VERSION%%.*}/wasi-sdk-$WASI_SDK_VERSION-$WASI_SDK_ARCH-linux.tar.gz" \
            | tar -xz -C "$WASM_TOOLS"
    fi

    if [ ! -x "$WASMTIME" ]; then
        echo "Downloading wasmtime $WASMTIME_VERSION ($WASMTIME_ARCH-linux)..."
        curl -fsSL "https://github.com/bytecodealliance/wasmtime/releases/download/$WASMTIME_VERSION/wasmtime-$WASMTIME_VERSION-$WASMTIME_ARCH-linux.tar.xz" \
            | tar -xJ -C "$WASM_TOOLS"
    fi

    echo "wasm:   $WASM_TOOLS (NativeAOT-LLVM $NAOT_LLVM_VERSION, WASI SDK $WASI_SDK_VERSION, wasmtime $WASMTIME_VERSION)"
}

if [ -x "$BFLAT_DIR/bflat" ]; then
    echo "bflat:  $BFLAT_DIR"
else
    echo "Missing: bflat fork layout at $BFLAT_DIR"
    echo "         Build it (README, \"Getting bflat\") or set BFLAT_DIR."
    ok=0
fi

if [ "$(uname -s)" = Darwin ]; then
    if command -v docker >/dev/null 2>&1; then
        echo "Building the $DOCKER_PLATFORM build container..."
        docker build --platform "$DOCKER_PLATFORM" -t "$IMAGE" docker
    else
        echo "Missing: Docker (bflat can't target macOS, so it runs in a Linux container)"
        ok=0
    fi
elif ! ldconfig -p 2>/dev/null | grep -q 'libtinfo.so.5'; then
    echo "Missing: libtinfo.so.5, needed by bflat's bundled linker."
    echo "         Ubuntu 22.04: sudo apt install libtinfo5 (Ubuntu 24.04 no longer ships it)"
    ok=0
fi

if [ "$(uname -s)" != Darwin ] && ! command -v as >/dev/null 2>&1; then
    echo "Missing: an assembler (as), for zerocore/native/*.S. Ubuntu: sudo apt install binutils"
    ok=0
fi

if [ "$want_wasm" = 1 ]; then
    fetch_wasm_tools
fi

if [ "$ok" = 1 ]; then
    if [ "$want_wasm" = 1 ]; then
        echo "Ready. Run ./build.sh --wasm"
    else
        echo "Ready. Run ./build.sh"
    fi
fi

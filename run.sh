#!/usr/bin/env bash
# Runs a built sample: natively on Linux, in the build container on macOS.
# With --wasm, runs bin/wasm/<Sample>.wasm with wasmtime (./setup.sh --wasm).
# With --dos, runs bin/dos/<Sample>.exe under FreeDOS in an emulator
# (./setup.sh --dos); --dos --window shows the DOS screen. See dos/run.sh.
# Usage: ./run.sh [--wasm | --dos [--window]] Arena
# Env:   BFLAT_ARCH (see env.sh): on macOS, runs a cross-compiled binary under emulation
set -euo pipefail
cd "$(dirname "$0")"
source ./env.sh

if [ "${1:-}" = --dos ]; then
    shift
    exec dos/run.sh "$@"
fi

wasm=0
if [ "${1:-}" = --wasm ]; then
    wasm=1
    shift
fi
if [ $# -lt 1 ]; then
    echo "Usage: ./run.sh [--wasm | --dos [--window]] <Sample> [args...]" >&2
    exit 1
fi
name=$1
shift

if [ "$wasm" = 1 ]; then
    command=("$WASMTIME" run "bin/wasm/$name.wasm" "$@")
else
    command=("./bin/$name" "$@")
fi

if [ "$(uname -s)" = Darwin ]; then
    # A binary cross-compiled with BFLAT_ARCH runs in a plain container of that
    # architecture, emulated by Docker. (WASM modules run on the host's tools.)
    if [ "$wasm" = 0 ] && [ "$BFLAT_ARCH" != "$HOST_ARCH" ]; then
        case "$BFLAT_ARCH" in
            x64)   platform=linux/amd64 ;;
            arm64) platform=linux/arm64 ;;
        esac
        exec docker run --rm --platform "$platform" -v "$PWD:/w" -w /w ubuntu:22.04 "${command[@]}"
    fi
    ensure_image
    exec docker run --rm --platform "$DOCKER_PLATFORM" -v "$PWD:/w" -w /w "$IMAGE" "${command[@]}"
fi
exec "${command[@]}"

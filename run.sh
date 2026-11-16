#!/usr/bin/env bash
# Runs a built sample: natively on Linux, in the build container on macOS.
# With --wasm, runs bin/wasm/<Sample>.wasm with wasmtime (./setup.sh --wasm).
# Usage: ./run.sh [--wasm] Arena
set -euo pipefail
cd "$(dirname "$0")"
source ./env.sh

wasm=0
if [ "${1:-}" = --wasm ]; then
    wasm=1
    shift
fi
if [ $# -lt 1 ]; then
    echo "Usage: ./run.sh [--wasm] <Sample> [args...]" >&2
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
    ensure_image
    exec docker run --rm --platform "$DOCKER_PLATFORM" -v "$PWD:/w" -w /w "$IMAGE" "${command[@]}"
fi
exec "${command[@]}"

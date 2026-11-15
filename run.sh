#!/usr/bin/env bash
# Runs a built sample: natively on Linux, in the build container on macOS.
# Usage: ./run.sh Arena
set -euo pipefail
cd "$(dirname "$0")"
source ./env.sh

if [ $# -lt 1 ]; then
    echo "Usage: ./run.sh <Sample> [args...]" >&2
    exit 1
fi
name=$1
shift

if [ "$(uname -s)" = Darwin ]; then
    ensure_image
    exec docker run --rm --platform "$DOCKER_PLATFORM" -v "$PWD:/w" -w /w "$IMAGE" "./bin/$name" "$@"
fi
exec "./bin/$name" "$@"

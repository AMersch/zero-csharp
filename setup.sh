#!/usr/bin/env bash
# Checks the prerequisites for build.sh and, on macOS, builds the container image.
#
# bflat comes from the fork (OnurGumus/bflat, branch csharp15), which must be
# built first; see README, "Getting bflat".
set -euo pipefail
cd "$(dirname "$0")"
source ./env.sh

ok=1

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

if [ "$ok" = 1 ]; then
    echo "Ready. Run ./build.sh"
fi

#!/usr/bin/env bash
# Runs bin/ios/<Sample> in the iOS simulator, with `xcrun simctl spawn`: no
# app bundle, the way a test runner starts a program there. The simulator is
# IOS_SIMULATOR (a name or UDID) if set, else the one ios/boot.sh picks and
# boots.
#
# Usage: ios/run.sh <Sample> [args...]   (or ./run.sh --ios <Sample> ...)
set -euo pipefail
caller=$PWD
cd "$(dirname "$0")/.."

if [ $# -lt 1 ]; then
    echo "Usage: ./run.sh --ios <Sample> [args...]" >&2
    exit 1
fi
name=$1
shift
program=$PWD/bin/ios/$name
if [ ! -x "$program" ]; then
    echo "Error: no $program (./build.sh --ios $name)" >&2
    exit 1
fi

device=$(sh ios/boot.sh "${IOS_SIMULATOR:-}")

# simctl starts programs in the simulated device's data directory. Simulator
# programs see the Mac's files, so ios/launch.c (built by ./build.sh --ios)
# moves to the caller's directory first, where relative paths then mean what
# they do with --mac.
exec xcrun simctl spawn "$device" "$PWD/bin/ios/obj/launch" "$caller" "$program" "$@"

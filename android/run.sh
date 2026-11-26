#!/usr/bin/env bash
# Runs bin/android/<Sample> on an Android phone or emulator, through adb: the
# program is copied to /data/local/tmp/zero, where adb shell may run
# programs, and runs there, with this terminal's input, output and exit code.
# android/boot.sh finds the device, or starts the emulator.
#
# With ANDROID_RUN=container, it runs in an arm64 Linux container instead, in
# this directory, with Android's loader and C library from android/sysroot
# (see there): no device, which is how CI runs it.
#
# Usage: android/run.sh <Sample> [args...]   (or ./run.sh --android <Sample> ...)
set -euo pipefail
caller=$PWD
cd "$(dirname "$0")/.."

if [ $# -lt 1 ]; then
    echo "Usage: ./run.sh --android <Sample> [args...]" >&2
    exit 1
fi
name=$1
shift
program=bin/android/$name
if [ ! -f "$program" ]; then
    echo "Error: no $program (./build.sh --android $name)" >&2
    exit 1
fi

if [ "${ANDROID_RUN:-}" = container ]; then
    tty=()
    [ -t 0 ] && tty=(-t)
    exec docker run --rm -i ${tty[@]+"${tty[@]}"} --platform linux/arm64 --user "$(id -u):$(id -g)" \
        -v "$PWD/android/sysroot/system:/system:ro" -v "$PWD/android/sysroot/linkerconfig:/linkerconfig:ro" \
        -v "$PWD/bin/android:/zero:ro" -v "$caller:/work" -w /work \
        ubuntu:24.04 "/zero/$name" "$@"
fi

adb=$(sh android/boot.sh < /dev/null)
dir=/data/local/tmp/zero
# Not from this terminal's input, which adb shell would read and the program
# then miss; adb push reports its progress on stderr, kept for an error.
"$adb" shell mkdir -p "$dir" < /dev/null > /dev/null
if ! out=$("$adb" push "$program" "$dir/$name" < /dev/null 2>&1); then
    echo "$out" >&2
    exit 1
fi

# adb shell joins its arguments into one command line for the device's
# shell, so each is quoted for it: '...', with ' as '\''.
command="cd $dir && chmod 755 ./$name && ./$name"
for argument in "$@"; do
    command+=" '${argument//\'/\'\\\'\'}'"
done
exec "$adb" shell -T "$command"

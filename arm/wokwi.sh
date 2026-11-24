#!/usr/bin/env bash
# Runs bin/pico2/board/<Sample> (the build for a real Raspberry Pi Pico 2,
# ./build.sh --pico2) on Wokwi's simulated Pico 2, an online RP2350 simulator
# (https://wokwi.com). Its console (UART0) is printed, and the script exits
# with the program's exit code, as arm/run.sh does for QEMU.
#
# What it's for: the parts of the board build QEMU can't run, because QEMU
# has no RP2350: the boot block, the clocks, the UART, the LED and
# interrupts (it found three bugs there). Not for arithmetic: Wokwi gets
# double division wrong in the last bits, which QEMU checks correctly.
#
# Needs a Wokwi CI token (free: https://wokwi.com/dashboard/ci) in
# WOKWI_CLI_TOKEN. Each run uses some of the account's simulation minutes.
# The first run downloads Wokwi's command-line tool into tools/arm.
#
# Usage: arm/wokwi.sh <Sample>
# Env:   ARM_TIMEOUT  seconds of simulated time before giving up (default 60)
set -euo pipefail
cd "$(dirname "$0")/.."
source ./env.sh

if [ $# -ne 1 ]; then
    echo "Usage: ./run.sh --pico2 --wokwi <Sample>" >&2
    exit 1
fi
name=$1
if [ ! -f "bin/pico2/board/$name.uf2" ]; then
    echo "No bin/pico2/board/$name.uf2: run ./build.sh --pico2 $name" >&2
    exit 1
fi
if [ -z "${WOKWI_CLI_TOKEN:-}" ]; then
    echo "Error: set WOKWI_CLI_TOKEN to a Wokwi CI token (free: https://wokwi.com/dashboard/ci)" >&2
    exit 1
fi

# Wokwi's command-line tool, checked against the hash of the file tested here.
version=0.29.1
case "$(uname -s)-$HOST_ARCH" in
    Darwin-arm64) file=macos-arm64;       sha256=94da96ee53af922506f0e4d3eb9bec0684a786c8fe27dc114cc84d27f653eb3d ;;
    Darwin-x64)   file=macos-x64;         sha256=27642917590f25f8d7671e5542cd9280a0397b9e326f336c14f72f4a26996cb3 ;;
    Linux-x64)    file=linuxstatic-x64;   sha256=3c9d6da007d008a3822ab468318037285440055132c59fd659d7306e40f14258 ;;
    Linux-arm64)  file=linuxstatic-arm64; sha256=ef442aad00f5cc2878dd239e77b1b3ca72c3f3292390b151b0d8818733b3eeee ;;
    *) echo "Error: no Wokwi CLI for this machine" >&2; exit 1 ;;
esac
cli=$ARM_TOOLS/wokwi-cli-$version
if [ ! -x "$cli" ]; then
    mkdir -p "$ARM_TOOLS"
    echo "Downloading Wokwi CLI $version ($file)..." >&2
    curl -fsSL -o "$cli.download" "https://github.com/wokwi/wokwi-cli/releases/download/v$version/wokwi-cli-$file"
    if command -v sha256sum >/dev/null 2>&1; then
        actual=$(sha256sum "$cli.download" | cut -d' ' -f1)
    else
        actual=$(shasum -a 256 "$cli.download" | cut -d' ' -f1)
    fi
    if [ "$actual" != "$sha256" ]; then
        rm -f "$cli.download"
        echo "Error: the Wokwi CLI download isn't the file tested here (SHA-256 differs)" >&2
        exit 1
    fi
    chmod +x "$cli.download"
    mv "$cli.download" "$cli"
fi

# A Wokwi project: a Pico 2 whose GP0/GP1 go to the serial monitor, running
# the program.
project=bin/pico2/wokwi/$name
rm -rf "$project"
mkdir -p "$project"
cp "bin/pico2/board/$name.uf2" "bin/pico2/board/$name.elf" "$project/"
cp arm/wokwi-pico2.json "$project/diagram.json"
printf '[wokwi]\nversion = 1\nfirmware = "%s.uf2"\nelf = "%s.elf"\n' "$name" "$name" > "$project/wokwi.toml"

# Until arm/rp2350.c's "--- exit code N ---" line, or the time limit.
timeout=${ARM_TIMEOUT:-60}
"$cli" "$project" --timeout $((timeout * 1000)) --expect-text "--- exit code" \
    --serial-log-file "$project/serial.txt" > "$project/wokwi.log" 2>&1 || true

console=$(tr -d '\r' < "$project/serial.txt")
code=$(echo "$console" | sed -n 's/^--- exit code \(-\{0,1\}[0-9]*\) ---$/\1/p' | head -1)
# The program's output: everything before the blank line and the exit line.
echo "$console" | awk '/^--- exit code/ { exit } { lines[n++] = $0 } END { while (n > 0 && lines[n-1] == "") n--; for (i = 0; i < n; i++) print lines[i] }'
if [ -z "$code" ]; then
    echo "arm/wokwi.sh: the program didn't finish in $timeout seconds (see $project/wokwi.log)" >&2
    exit 124
fi
exit "$code"

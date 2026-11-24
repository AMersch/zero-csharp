#!/usr/bin/env bash
# Runs bin/pico2/board/<Sample> (./build.sh --pico2) on a real Raspberry Pi
# Pico 2, then prints what it writes to its console until it ends, and exits
# with its exit code.
#
# Installing the program, the first way that works:
#   1. A Pico 2 in BOOTSEL mode (held BOOTSEL while plugging it in), which
#      shows up as a drive named RP2350: the .uf2 file is copied to it.
#   2. A Raspberry Pi Debug Probe (or another CMSIS-DAP probe) wired to the
#      Pico 2's debug pins, and OpenOCD with RP2350 support: the .elf file
#      is written to flash over it.
#
# The console is UART0 on GP0 (TX) and GP1 (RX) at 115200 baud: the Debug
# Probe's UART port, or any 3.3 V USB-serial adapter. Its device is
# PICO_SERIAL, or the first /dev/cu.usbmodem* (macOS) or /dev/ttyACM*
# (Linux). For typing to a program (Quiz), use a terminal program instead:
# screen /dev/cu.usbmodem... 115200, or picocom -b 115200 /dev/ttyACM0.
#
# Usage: arm/board.sh <Sample>
set -euo pipefail
cd "$(dirname "$0")/.."

if [ $# -ne 1 ]; then
    echo "Usage: ./run.sh --pico2 --board <Sample>" >&2
    exit 1
fi
name=$1
elf=bin/pico2/board/$name.elf
uf2=bin/pico2/board/$name.uf2
if [ ! -f "$uf2" ]; then
    echo "No $uf2: run ./build.sh --pico2 $name" >&2
    exit 1
fi

find_serial() {
    local device
    if [ -n "${PICO_SERIAL:-}" ]; then
        echo "$PICO_SERIAL"
        return 0
    fi
    for device in /dev/cu.usbmodem* /dev/ttyACM*; do
        if [ -e "$device" ]; then
            echo "$device"
            return 0
        fi
    done
    return 1
}

# Start listening before the program starts, so nothing it prints is lost.
serial=
if serial=$(find_serial); then
    if [ "$(uname -s)" = Darwin ]; then
        stty -f "$serial" 115200 raw -echo
    else
        stty -F "$serial" 115200 raw -echo
    fi
    output=$(mktemp)
    trap 'rm -f "$output"; kill "$listener" 2>/dev/null || true' EXIT
    cat "$serial" > "$output" &
    listener=$!
fi

drive=
for candidate in /Volumes/RP2350 "/media/${USER:-}/RP2350" "/run/media/${USER:-}/RP2350"; do
    if [ -d "$candidate" ]; then
        drive=$candidate
        break
    fi
done
if [ -n "$drive" ]; then
    echo "Copying $uf2 to $drive..." >&2
    cp "$uf2" "$drive/"
elif command -v openocd >/dev/null 2>&1; then
    echo "Writing $elf to flash with OpenOCD..." >&2
    openocd -f interface/cmsis-dap.cfg -f target/rp2350.cfg -c "adapter speed 5000" \
        -c "program $elf verify reset exit" >&2
else
    echo "No Pico 2 to install on: hold BOOTSEL while plugging it in (it shows up as a drive" >&2
    echo "named RP2350), or connect a Debug Probe and install OpenOCD (brew install open-ocd)." >&2
    exit 1
fi

if [ -z "$serial" ]; then
    echo "Installed. No serial port found for its console (set PICO_SERIAL to one)." >&2
    exit 0
fi

# Print what arrives until the "--- exit code N ---" line rp2350.c ends with.
timeout=${ARM_TIMEOUT:-60}
shown=0
for ((tick = 0; tick < timeout * 10; tick++)); do
    sleep 0.1
    size=$(wc -c < "$output")
    if [ "$size" -gt "$shown" ]; then
        tail -c +$((shown + 1)) "$output" | head -c $((size - shown)) | tr -d '\r'
        shown=$size
    fi
    if code=$(tr -d '\r' < "$output" | sed -n 's/^--- exit code \(-\{0,1\}[0-9]*\) ---$/\1/p' | head -1) && [ -n "$code" ]; then
        exit "$code"
    fi
done
echo "arm/board.sh: no exit after $timeout seconds" >&2
exit 124

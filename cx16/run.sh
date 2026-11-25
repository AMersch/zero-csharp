#!/usr/bin/env bash
# Runs bin/cx16/<Sample>.prg on an emulated Commander X16 (x16emu, the
# community emulator). Called by ../run.sh --cx16.
#
# Without --window the emulator runs headless (-testbench) and as fast as the
# host can (-warp), and this script prints what the program printed and exits
# with the program's exit code. An X16 has neither a standard output nor an
# exit code, so both come through the emulator's debug registers
# (cx16/runtime.c):
#   - Output: every character is also written to $9FBB, which the emulator
#     prints on its standard output as it is.
#   - Exit code: written to $9FB9, which the emulator reports as the line
#     "User debug 1: $xx". The script ends the emulator when it sees it.
#
# With --window, the emulator runs at a real X16's speed, with its screen,
# sound and keyboard; close the window to end.
#
# Usage: cx16/run.sh [--window] <Sample>
# Env:   X16_EMULATOR  x16emu; default: x16emu on the PATH, or see env.sh
#        X16_ROM       its ROM, if it isn't next to the emulator
#        X16_TIMEOUT   seconds the program may take without a window (default 600)
#
# When standard input is a file or a pipe, its text is what the program reads
# (as lines with In.ReadLine, or as keys with Console.ReadKey).
set -euo pipefail
cd "$(dirname "$0")/.."
source ./env.sh

window=0
if [ "${1:-}" = --window ]; then
    window=1
    shift
fi
if [ $# -lt 1 ]; then
    echo "Usage: ./run.sh --cx16 [--window] <Sample>" >&2
    exit 1
fi
name=$1
if [ $# -gt 1 ]; then
    echo "Error: an X16 program can't be given arguments." >&2
    exit 1
fi

program=bin/cx16/$name.prg
if [ ! -f "$program" ]; then
    echo "Error: no $program. Run ./build.sh --cx16 $name" >&2
    exit 1
fi
if ! emulator=$(find_x16_emulator); then
    echo "Error: no x16emu. Get it from https://github.com/X16Community/x16-emulator, or set X16_EMULATOR." >&2
    exit 1
fi

options=(-prg "$program" -run -fsroot "$(dirname "$program")")
[ -n "${X16_ROM:-}" ] && options+=(-rom "$X16_ROM")

if [ "$window" = 1 ]; then
    exec "$emulator" "${options[@]}" -scale 2 > /dev/null 2>&1
fi

# Text for the program to read: the emulator can't type it, so it goes in a
# cartridge image that cx16/runtime.c reads (see there).
work=$PWD/bin/cx16/run
mkdir -p "$work"
if [ -f /dev/stdin ] || [ -p /dev/stdin ]; then
    python3 -I cx16/input.py > "$work/input.bin"
    options+=(-cartbin "$work/input.bin")
fi

exec python3 -I cx16/watch.py "${X16_TIMEOUT:-600}" "$emulator" "${options[@]}" -warp -testbench

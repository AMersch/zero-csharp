#!/usr/bin/env bash
# Runs bin/dos/<Sample>.exe under FreeDOS in an emulator. Called by
# ../run.sh --dos; ./setup.sh --dos downloads FreeDOS and the HX DOS extender.
#
# The emulated machine boots FreeDOS from one disk and gets a second one (D:),
# made here, holding the program, the HX runtime and a batch file that runs it.
# Without --window there is no screen: the program's output goes to a file on
# D:, which is printed here afterwards, and this script exits with the
# program's exit code. With --window the emulator's window shows the DOS
# screen and stays at the prompt (type `PROGRAM` to run it again).
#
# Usage: dos/run.sh [--window] <Sample> [args...] [< input]
# When standard input is a file or a pipe, its contents go onto D: and the
# program reads them as its standard input; otherwise it reads the keyboard.
# Env:   DOS_EMULATOR, DOS_CPU, DOS_TOOLS (see ../env.sh)
#        DOS_TIMEOUT   seconds before a run without a window is stopped (default 120)
#        DOS_MEMORY    the machine's memory in MB (default 4)
#        DOS_BOCHS_DISPLAY  Bochs's display library for --window (default sdl2)
#        DOS_IPS       Bochs only: instructions per second (default 50000000).
#                      The i386 model limits which instructions run, not how
#                      fast; about 6000000 is the speed of a 386DX-33.
set -euo pipefail
cd "$(dirname "$0")/.."
source ./env.sh

window=0
if [ "${1:-}" = --window ]; then
    window=1
    shift
fi
if [ $# -lt 1 ]; then
    echo "Usage: ./run.sh --dos [--window] <Sample> [args...]" >&2
    exit 1
fi
name=$1
shift

exe=bin/dos/$name.exe
if [ ! -f "$exe" ]; then
    echo "Error: no $exe. Run ./build.sh --dos $name" >&2
    exit 1
fi
if [ ! -f "$DOS_BOOT" ] || [ ! -f "$DOS_HX/DPMILD32.EXE" ]; then
    echo "Error: no FreeDOS or HX runtime in $DOS_TOOLS. Run ./setup.sh --dos" >&2
    exit 1
fi
if ! emulator=$(find_dos_emulator); then
    echo "Error: no emulator. Install bochs or qemu-system-i386, or set DOS_EMULATOR." >&2
    exit 1
fi

work=bin/dos/run
memory=${DOS_MEMORY:-4}
mkdir -p "$work"
disk=$work/disk.img

# Standard input from a file or a pipe becomes IN.TXT, which the batch file
# hands to the program with "<". (With a window, the program reads the keyboard.)
input=
if [ "$window" = 0 ] && { [ -f /dev/stdin ] || [ -p /dev/stdin ]; }; then
    cat > "$work/IN.TXT"
    input=" < IN.TXT"
fi

# HXLDR32 stays resident and makes DOS start Windows console programs through
# HX's loader, so the program is run by its name like any DOS program.
# CTMOUSE is FreeDOS's mouse driver; without a mouse it just doesn't load.
# The program's arguments as a DOS command line: one with a space in it is
# quoted, and the program's startup code (dos/runtime.c) splits it back.
arguments=
for argument in "$@"; do
    case "$argument" in
        *" "*) arguments+=" \"$argument\"" ;;
        *) arguments+=" $argument" ;;
    esac
done

files=("$exe=PROGRAM.EXE" "$work/RUN.BAT"
    "$DOS_HX/HXLDR32.EXE" "$DOS_HX/DPMILD32.EXE" "$DOS_HX/HDPMI32.EXE" "$DOS_HX/DKRNL32.DLL")
{
    printf '@echo off\r\nD:\r\nSET PATH=%%PATH%%;D:\\\r\nHXLDR32 > NUL\r\n'
    if [ -f "$DOS_MOUSE" ]; then
        printf 'CTMOUSE > NUL\r\n'
        files+=("$DOS_MOUSE")
    fi
    if [ "$window" = 1 ]; then
        printf 'PROGRAM.EXE%s\r\n' "$arguments"
    else
        printf 'PROGRAM.EXE%s%s > OUT.TXT\r\n' "$arguments" "$input"
        printf 'echo %%ERRORLEVEL%% > CODE.TXT\r\n'
        printf 'FDAPM POWEROFF\r\n'
    fi
} > "$work/RUN.BAT"

if [ -n "$input" ]; then
    files+=("$work/IN.TXT")
fi
python3 dos/fat16.py make "$disk" "${files[@]}"

case "$emulator" in
    bochs)
        cpu=${DOS_CPU:-i386}
        # volatile: changes to the FreeDOS disk are dropped when Bochs exits.
        # The "legacy" BIOS is the one written for machines of this age.
        {
            echo "cpu: model=$cpu, ips=${DOS_IPS:-50000000}"
            echo "memory: guest=$memory, host=$memory"
            echo 'romimage: file=$BXSHARE/BIOS-bochs-legacy'
            echo 'vgaromimage: file=$BXSHARE/VGABIOS-lgpl-latest.bin'
            echo "ata0: enabled=1, ioaddr1=0x1f0, ioaddr2=0x3f0, irq=14"
            echo "ata0-master: type=disk, path=$DOS_BOOT, mode=volatile"
            echo "ata0-slave: type=disk, path=$disk, mode=flat"
            echo "boot: disk"
            echo "log: $work/bochs.log"
            echo "panic: action=fatal"
            echo "error: action=report"
            # Bochs's default display isn't built into every package (Homebrew's
            # just exits), so name one: SDL2, or what DOS_BOCHS_DISPLAY says.
            if [ "$window" = 1 ]; then
                echo "display_library: ${DOS_BOCHS_DISPLAY:-sdl2}"
            else
                echo "display_library: nogui"
            fi
        } > "$work/bochsrc"
        command=(bochs -q -f "$work/bochsrc")
        ;;
    qemu)
        cpu=${DOS_CPU:-486}
        command=(qemu-system-i386 -cpu "$cpu" -m "$memory"
            -drive "file=$DOS_BOOT,format=raw,index=0,media=disk,snapshot=on"
            -drive "file=$disk,format=raw,index=1,media=disk")
        [ "$window" = 1 ] || command+=(-display none)
        ;;
    *)
        echo "Error: DOS_EMULATOR must be bochs or qemu, not '$emulator'" >&2
        exit 1
        ;;
esac

if [ "$window" = 1 ]; then
    echo "Starting $emulator ($cpu). The program is D:\\PROGRAM.EXE; close the window to end." >&2
    "${command[@]}" > "$work/emulator.log" 2>&1 || true
    exit 0
fi

# The batch file powers the machine off when the program ends, which ends the
# emulator. If that doesn't happen (the program hangs or crashes DOS), stop it.
"${command[@]}" < /dev/null > "$work/emulator.log" 2>&1 &
pid=$!
deadline=$((SECONDS + ${DOS_TIMEOUT:-120}))
while kill -0 "$pid" 2>/dev/null; do
    if [ "$SECONDS" -ge "$deadline" ]; then
        kill "$pid" 2>/dev/null || true
        break
    fi
    sleep 0.2
done
wait "$pid" 2>/dev/null || true

# Whatever the program wrote before it ended, even if it didn't end well.
python3 dos/fat16.py read "$disk" OUT.TXT 2>/dev/null || true

if ! code=$(python3 dos/fat16.py read "$disk" CODE.TXT 2>/dev/null); then
    echo "Error: $name didn't finish under DOS ($emulator, $cpu). See $work/emulator.log" >&2
    exit 125
fi
code=$(printf '%s' "$code" | tr -dc '0-9')
exit "${code:-125}"

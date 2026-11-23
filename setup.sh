#!/usr/bin/env bash
# Checks the prerequisites for build.sh and, on macOS, builds the container image.
# With --wasm, also downloads the WebAssembly toolchain (see env.sh) into
# $WASM_TOOLS: about 900 MB unpacked, most of it the WASI SDK.
#
# With --dos, downloads what DOS builds need into $WASM_TOOLS and $DOS_TOOLS:
# NativeAOT-LLVM (shared with --wasm), FreeDOS and the HX DOS extender, about
# 75 MB. It also checks for a clang with the x86 target and an emulator.
#
# With --c64, downloads what Commodore 64 builds need into $WASM_TOOLS and
# $C64_TOOLS: NativeAOT-LLVM (shared with --wasm) and llvm-mos, the compiler
# for the 6502, about 750 MB unpacked. It also checks for VICE, the emulator.
#
# bflat comes from the fork (OnurGumus/bflat, branch master), which must be
# built first; see README, "Getting bflat".
#
# Usage: ./setup.sh [--wasm | --dos | --c64]
set -euo pipefail
cd "$(dirname "$0")"
source ./env.sh

ok=1
want_wasm=0
want_dos=0
want_c64=0
[ "${1:-}" = --wasm ] && want_wasm=1
[ "${1:-}" = --dos ] && want_dos=1
[ "${1:-}" = --c64 ] && want_c64=1

# NativeAOT-LLVM: the compiler from C# (IL) to LLVM bitcode.
fetch_ilc() {
    mkdir -p "$WASM_TOOLS"

    if [ ! -x "$WASM_ILC" ]; then
        local id="runtime.$WASM_HOST_NUGET.microsoft.dotnet.ilcompiler.llvm"
        local dir="$WASM_TOOLS/ilc-$NAOT_LLVM_VERSION"
        echo "Downloading NativeAOT-LLVM $NAOT_LLVM_VERSION ($WASM_HOST_NUGET)..."
        curl -fsSL -o "$WASM_TOOLS/ilc.nupkg" \
            "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-experimental/nuget/v3/flat2/$id/$NAOT_LLVM_VERSION/$id.$NAOT_LLVM_VERSION.nupkg"
        rm -rf "$dir"
        mkdir -p "$dir"
        unzip -q "$WASM_TOOLS/ilc.nupkg" -d "$dir"
        rm "$WASM_TOOLS/ilc.nupkg"
        chmod +x "$WASM_ILC"
    fi
}

fetch_wasm_tools() {
    fetch_ilc

    if [ ! -x "$WASI_SDK/bin/clang" ]; then
        echo "Downloading WASI SDK $WASI_SDK_VERSION ($WASI_SDK_ARCH-linux)..."
        curl -fsSL "https://github.com/WebAssembly/wasi-sdk/releases/download/wasi-sdk-${WASI_SDK_VERSION%%.*}/wasi-sdk-$WASI_SDK_VERSION-$WASI_SDK_ARCH-linux.tar.gz" \
            | tar -xz -C "$WASM_TOOLS"
    fi

    if [ ! -x "$WASMTIME" ]; then
        echo "Downloading wasmtime $WASMTIME_VERSION ($WASMTIME_ARCH-linux)..."
        curl -fsSL "https://github.com/bytecodealliance/wasmtime/releases/download/$WASMTIME_VERSION/wasmtime-$WASMTIME_VERSION-$WASMTIME_ARCH-linux.tar.xz" \
            | tar -xJ -C "$WASM_TOOLS"
    fi

    echo "wasm:   $WASM_TOOLS (NativeAOT-LLVM $NAOT_LLVM_VERSION, WASI SDK $WASI_SDK_VERSION, wasmtime $WASMTIME_VERSION)"
}

# FreeDOS to boot the emulator with, and the HX DOS extender, which runs 32-bit
# Windows console programs (what build.sh --dos produces) under DOS.
fetch_dos_tools() {
    fetch_ilc
    mkdir -p "$DOS_TOOLS"

    if [ ! -f "$DOS_HX/DPMILD32.EXE" ]; then
        local zip="$DOS_TOOLS/hxrt.zip"
        echo "Downloading the HX DOS extender runtime $HX_VERSION..."
        curl -fsSL -o "$zip" \
            "https://github.com/Baron-von-Riedesel/HX/releases/download/v$HX_VERSION/HXRT${HX_VERSION//./}.zip"
        mkdir -p "$DOS_HX"
        # The DPMI host, the loader for Windows programs and its resident
        # part, and HX's kernel32; plus the license terms.
        unzip -q -o -j "$zip" BIN/HDPMI32.EXE BIN/DPMILD32.EXE BIN/HXLDR32.EXE BIN/DKRNL32.DLL HXRT.TXT -d "$DOS_HX"
        rm "$zip"
    fi

    if [ ! -f "$DOS_BOOT" ]; then
        local zip="$DOS_TOOLS/freedos.zip"
        echo "Downloading FreeDOS $FREEDOS_VERSION (the Lite USB image)..."
        curl -fsSL -o "$zip" \
            "https://www.ibiblio.org/pub/micro/pc-stuff/freedos/files/distributions/$FREEDOS_VERSION/FD${FREEDOS_VERSION//./}-LiteUSB.zip"
        unzip -q -o -j "$zip" "FD${FREEDOS_VERSION//./}LITE.img" -d "$DOS_TOOLS"
        rm "$zip"
        # The image boots into FreeDOS's installer. Replace its startup file
        # with one that runs the program disk (drive D:) that run.sh attaches.
        printf '@echo off\r\nSET DOSDIR=\\FREEDOS\r\nSET PATH=C:\\FREEDOS\\BIN\r\nif exist D:\\RUN.BAT D:\\RUN.BAT\r\n' > "$DOS_TOOLS/fdauto.bat"
        python3 dos/fat16.py replace "$DOS_TOOLS/FD${FREEDOS_VERSION//./}LITE.img" FDAUTO.BAT "$DOS_TOOLS/fdauto.bat"
        rm "$DOS_TOOLS/fdauto.bat"
        mv "$DOS_TOOLS/FD${FREEDOS_VERSION//./}LITE.img" "$DOS_BOOT"
    fi
    if [ ! -f "$DOS_MOUSE" ]; then
        # CuteMouse, the mouse driver FreeDOS ships as a package on its image.
        python3 dos/fat16.py read "$DOS_BOOT" PACKAGES/BASE/CTMOUSE.ZIP > "$DOS_TOOLS/ctmouse.zip"
        unzip -q -o -j "$DOS_TOOLS/ctmouse.zip" BIN/CTMOUSE.EXE -d "$DOS_TOOLS"
        rm "$DOS_TOOLS/ctmouse.zip"
    fi
    echo "dos:    $DOS_TOOLS (FreeDOS $FREEDOS_VERSION, HX $HX_VERSION)"

    local clang emulator
    if clang=$(find_dos_clang); then
        echo "clang:  $clang"
    else
        echo "Missing: a clang that can compile for 32-bit x86 (clang -target i386-pc-windows-gnu)."
        echo "         macOS: install Xcode or its command line tools. Linux: install clang. Or set DOS_CLANG."
        ok=0
    fi
    if emulator=$(find_dos_emulator); then
        echo "emulator: $emulator"
    else
        echo "Missing: an emulator for run.sh --dos: bochs (checks the 386 instruction set) or qemu-system-i386."
        echo "         macOS: brew install bochs. Ubuntu: sudo apt install bochs (or qemu-system-x86)."
        ok=0
    fi
}

# llvm-mos: clang and a linker for the 6502, with a C library and startup
# code for the C64. Its releases are builds for x64 Linux and for macOS.
fetch_c64_tools() {
    fetch_ilc
    mkdir -p "$C64_TOOLS"

    if [ ! -x "$LLVM_MOS/bin/mos-c64-clang" ]; then
        local archive host
        case "$(uname -s)-$HOST_ARCH" in
            Darwin-*)  host=macos ;;
            Linux-x64) host=linux ;;
            *) echo "Missing: llvm-mos has no build for this machine ($(uname -s), $HOST_ARCH)."
               echo "         Build it from https://github.com/llvm-mos/llvm-mos-sdk and unpack it as $LLVM_MOS"
               ok=0
               return ;;
        esac
        archive=$C64_TOOLS/llvm-mos.tar.xz
        echo "Downloading llvm-mos $LLVM_MOS_VERSION ($host, about 190 MB)..."
        curl -fsSL -o "$archive" \
            "https://github.com/llvm-mos/llvm-mos-sdk/releases/download/$LLVM_MOS_VERSION/llvm-mos-$host.tar.xz"
        rm -rf "$C64_TOOLS/llvm-mos" "$LLVM_MOS"
        tar -xf "$archive" -C "$C64_TOOLS"
        mv "$C64_TOOLS/llvm-mos" "$LLVM_MOS"
        rm "$archive"
    fi
    echo "c64:    $LLVM_MOS"

    local emulator
    if emulator=$(find_c64_emulator); then
        echo "vice:   $emulator"
    else
        echo "Missing: VICE, the emulator run.sh --c64 uses (x64sc). macOS: brew install vice. Linux: apt install vice"
        ok=0
    fi

    # Homebrew's VICE has the ROMs; Debian's and Ubuntu's don't.
    if [ "$(uname -s)" != Darwin ] && [ ! -f "$C64_ROMS/C64/kernal-901227-03.bin" ]; then
        local archive=$C64_TOOLS/vice.tar.gz
        echo "Downloading the C64's ROMs from VICE $VICE_ROMS_VERSION's source release (13 MB)..."
        curl -fsSL -o "$archive" \
            "https://sourceforge.net/projects/vice-emu/files/releases/vice-$VICE_ROMS_VERSION.tar.gz/download"
        rm -rf "$C64_ROMS"
        mkdir -p "$C64_ROMS"
        tar -xzf "$archive" -C "$C64_ROMS" --strip-components=2 \
            "vice-$VICE_ROMS_VERSION/data/C64/kernal-901227-03.bin" \
            "vice-$VICE_ROMS_VERSION/data/C64/basic-901226-01.bin" \
            "vice-$VICE_ROMS_VERSION/data/C64/chargen-901225-01.bin" \
            "vice-$VICE_ROMS_VERSION/data/DRIVES"
        rm "$archive"
    fi
    [ -d "$C64_ROMS" ] && echo "roms:   $C64_ROMS"
}

if ! command -v python3 >/dev/null 2>&1 && { [ "$want_dos" = 1 ] || [ "$want_c64" = 1 ]; }; then
    echo "Missing: python3, for the tools in dos/ and c64/"
    ok=0
    want_dos=0
    want_c64=0
fi

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
        echo "Missing: Docker (bflat is a Linux program, so it runs in a Linux container)"
        ok=0
    fi
elif ! ldconfig -p 2>/dev/null | grep -q 'libtinfo.so.5'; then
    echo "Missing: libtinfo.so.5, needed by bflat's bundled linker."
    echo "         Ubuntu 22.04: sudo apt install libtinfo5 (Ubuntu 24.04 no longer ships it)"
    ok=0
fi

if [ "$(uname -s)" != Darwin ] && ! command -v as >/dev/null 2>&1; then
    echo "Missing: an assembler (as), for zerocore/native/*.S. Ubuntu: sudo apt install binutils"
    ok=0
fi

if [ "$want_wasm" = 1 ]; then
    fetch_wasm_tools
fi
if [ "$want_dos" = 1 ]; then
    fetch_dos_tools
fi
if [ "$want_c64" = 1 ]; then
    fetch_c64_tools
fi

if [ "$ok" = 1 ]; then
    if [ "$want_wasm" = 1 ]; then
        echo "Ready. Run ./build.sh --wasm"
    elif [ "$want_dos" = 1 ]; then
        echo "Ready. Run ./build.sh --dos"
    elif [ "$want_c64" = 1 ]; then
        echo "Ready. Run ./build.sh --c64"
    else
        echo "Ready. Run ./build.sh"
    fi
fi

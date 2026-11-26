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
# With --arm, downloads what Cortex-M builds (--pico2, --r4) need into
# $WASM_TOOLS and $ARM_TOOLS: NativeAOT-LLVM (shared with --wasm) and Arm
# Toolchain for Embedded, about 2 GB unpacked. It also checks for QEMU.
#
# With --cx16, downloads what Commander X16 builds need: the same NativeAOT-LLVM
# and llvm-mos as --c64. It also checks for x16emu, the emulator, and its ROM,
# which it doesn't download (see env.sh for where they are looked for).
#
# bflat comes from the fork (OnurGumus/bflat, branch master), which must be
# built first; see README, "Getting bflat".
#
# Usage: ./setup.sh [--wasm | --dos | --c64 | --cx16 | --arm]
set -euo pipefail
cd "$(dirname "$0")"
source ./env.sh

ok=1
want_wasm=0
want_dos=0
want_c64=0
want_cx16=0
want_arm=0
[ "${1:-}" = --wasm ] && want_wasm=1
[ "${1:-}" = --dos ] && want_dos=1
[ "${1:-}" = --c64 ] && want_c64=1
[ "${1:-}" = --cx16 ] && want_cx16=1
case "${1:-}" in --arm | --pico2 | --r4) want_arm=1 ;; esac

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
# code for the C64 and the Commander X16. Its releases are builds for x64
# Linux and for macOS; for another machine it says so and leaves $LLVM_MOS
# unmade. (Called on its own, not in a condition: that would switch set -e off.)
fetch_llvm_mos() {
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
               return 0 ;;
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
}

fetch_c64_tools() {
    fetch_llvm_mos
    [ -x "$LLVM_MOS/bin/mos-c64-clang" ] || return 0
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
    # (an if: as the function's last command, a failed test would end the script)
    if [ -d "$C64_ROMS" ]; then
        echo "roms:   $C64_ROMS"
    fi
}

# The Commander X16: llvm-mos, and a check for x16emu and its ROM. The emulator
# looks for rom.bin in its own folder unless it is given one.
fetch_cx16_tools() {
    fetch_llvm_mos
    [ -x "$LLVM_MOS/bin/mos-cx16-clang" ] || return 0
    echo "cx16:   $LLVM_MOS"

    local emulator
    if ! emulator=$(find_x16_emulator); then
        echo "Missing: x16emu, the emulator run.sh --cx16 uses: https://github.com/X16Community/x16-emulator/releases"
        echo "         Put it on the PATH or in ~/X16, or set X16_EMULATOR (or its folder as X16_HOME)."
        ok=0
        return 0
    fi
    # It says its release ("### Release 49 ...") and ends.
    local release
    release=$("$emulator" -version 2>/dev/null | head -n 1 || true)
    echo "x16emu: $emulator${release:+ (${release#\#\#\# })}"

    local rom=${X16_ROM:-}
    if [ -z "$rom" ]; then
        # Next to the emulator, and that is where a link to it leads.
        local real
        real=$(python3 -c 'import os, sys; print(os.path.realpath(sys.argv[1]))' "$emulator" 2>/dev/null || echo "$emulator")
        rom=$(dirname "$real")/rom.bin
    fi
    if [ -f "$rom" ]; then
        echo "rom:    $rom"
    else
        echo "Missing: the X16's ROM, $rom"
        echo "         It comes with the emulator's release; set X16_ROM if it is somewhere else."
        ok=0
    fi
}

# Arm Toolchain for Embedded: clang, lld, and picolibc and compiler-rt built
# for every Cortex-M. Arm builds it for macOS (a disk image, for both kinds of
# Mac), x64 and arm64 Linux, and x64 Windows.
fetch_arm_tools() {
    fetch_ilc
    mkdir -p "$ARM_TOOLS"

    if [ ! -x "$ARM_TOOLCHAIN/bin/clang" ]; then
        local file sha256 archive mount actual
        case "$(uname -s)-$HOST_ARCH" in
            Darwin-*)    file=Darwin-universal.dmg; sha256=6b7ec8861cd7afdd0f872eed661cf87a6ffd289289de314df53b9a43b0547e00 ;;
            Linux-x64)   file=Linux-x86_64.tar.xz;  sha256=a7be511613af15151c93961ad39c8cf9ae6889a453c8b547f42f4051a416dc8e ;;
            Linux-arm64) file=Linux-AArch64.tar.xz; sha256=df3b055d05659d8fda710d820e6c1ceff7bca3234c5067abe82a16e781d8de99 ;;
            *) echo "Missing: Arm Toolchain for Embedded has no build for this machine ($(uname -s), $HOST_ARCH)."
               ok=0
               return ;;
        esac
        file=ATfE-$ARM_TOOLCHAIN_VERSION-$file
        archive=$ARM_TOOLS/$file
        echo "Downloading Arm Toolchain for Embedded $ARM_TOOLCHAIN_VERSION ($file)..."
        curl -fsSL -o "$archive" \
            "https://github.com/arm/arm-toolchain/releases/download/release-$ARM_TOOLCHAIN_VERSION-ATfE/$file"
        if command -v sha256sum >/dev/null 2>&1; then
            actual=$(sha256sum "$archive" | cut -d' ' -f1)
        else
            actual=$(shasum -a 256 "$archive" | cut -d' ' -f1)
        fi
        if [ "$actual" != "$sha256" ]; then
            echo "Error: $archive isn't the file Arm published (SHA-256 differs)." >&2
            rm -f "$archive"
            exit 1
        fi
        rm -rf "$ARM_TOOLCHAIN" "$ARM_TOOLS/unpack"
        mkdir -p "$ARM_TOOLS/unpack"
        case "$file" in
            *.dmg)
                mount=$(mktemp -d)
                hdiutil attach -quiet -nobrowse -readonly -mountpoint "$mount" "$archive"
                cp -R "$mount"/ATfE-* "$ARM_TOOLS/unpack/"
                hdiutil detach -quiet "$mount"
                rmdir "$mount" ;;
            *) tar -xJf "$archive" -C "$ARM_TOOLS/unpack" ;;
        esac
        mv "$ARM_TOOLS/unpack"/ATfE-* "$ARM_TOOLCHAIN"
        rm -rf "$ARM_TOOLS/unpack" "$archive"
    fi
    echo "arm:    $ARM_TOOLCHAIN"

    if command -v qemu-system-arm >/dev/null 2>&1; then
        echo "qemu:   $(command -v qemu-system-arm)"
    else
        echo "Missing: QEMU, which run.sh --pico2 and --r4 use (qemu-system-arm). macOS: brew install qemu. Ubuntu: apt install qemu-system-arm"
        ok=0
    fi
}

if ! command -v python3 >/dev/null 2>&1 && { [ "$want_dos" = 1 ] || [ "$want_c64" = 1 ] || [ "$want_cx16" = 1 ]; }; then
    echo "Missing: python3, which dos/run.sh, c64/run.sh and cx16/run.sh use to run programs in the emulator"
    ok=0
fi

if ! command -v dotnet >/dev/null 2>&1 && { [ "$want_dos" = 1 ] || [ "$want_c64" = 1 ] || [ "$want_cx16" = 1 ] || [ "$want_arm" = 1 ]; }; then
    echo "Missing: the .NET SDK (10 or newer), to build zero-bitcode (sdk/ZeroCSharp.Bitcode) for DOS, the C64, the X16 and Cortex-M"
    ok=0
    want_dos=0
    want_c64=0
    want_cx16=0
    want_arm=0
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
if [ "$want_cx16" = 1 ]; then
    fetch_cx16_tools
fi
if [ "$want_arm" = 1 ]; then
    fetch_arm_tools
fi

if [ "$ok" = 1 ]; then
    if [ "$want_wasm" = 1 ]; then
        echo "Ready. Run ./build.sh --wasm"
    elif [ "$want_dos" = 1 ]; then
        echo "Ready. Run ./build.sh --dos"
    elif [ "$want_c64" = 1 ]; then
        echo "Ready. Run ./build.sh --c64"
    elif [ "$want_cx16" = 1 ]; then
        echo "Ready. Run ./build.sh --cx16"
    elif [ "$want_arm" = 1 ]; then
        echo "Ready. Run ./build.sh --pico2 or ./build.sh --r4"
    else
        echo "Ready. Run ./build.sh"
    fi
fi

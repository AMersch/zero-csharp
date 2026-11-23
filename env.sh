# Shared settings for setup.sh, build.sh and run.sh. Sourced, not executed.
#
# BFLAT_DIR   bflat install to use. Default: the bflat fork's layout for this
#             machine's architecture, ../bflat/layouts/linux-glibc-<arch>
#             (see README, "Getting bflat").
# BFLAT_FORK  where the fork is checked out. Default: ../bflat
# BFLAT_ARCH  target architecture (x64 or arm64). Default: this machine's.

case "$(uname -m)" in
    x86_64 | amd64)  HOST_ARCH=x64;   DOCKER_PLATFORM=linux/amd64 ;;
    arm64 | aarch64) HOST_ARCH=arm64; DOCKER_PLATFORM=linux/arm64 ;;
    *) echo "Unsupported architecture: $(uname -m)" >&2; exit 1 ;;
esac

BFLAT_ARCH=${BFLAT_ARCH:-$HOST_ARCH}
BFLAT_DIR=${BFLAT_DIR:-${BFLAT_FORK:-../bflat}/layouts/linux-glibc-$HOST_ARCH}

# bflat is a Linux program, so on macOS it runs in a Linux container of the
# Mac's own architecture (native speed on Apple Silicon). It can't link macOS
# programs; ./build.sh --mac has it compile only, and links on the Mac.
IMAGE="bflat-zero-build:2-$HOST_ARCH"

ensure_image() {
    if ! command -v docker >/dev/null 2>&1; then
        echo "Error: Docker is required on macOS (bflat runs in a Linux container)." >&2
        exit 1
    fi
    if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
        docker build --platform "$DOCKER_PLATFORM" -t "$IMAGE" docker
    fi
}

# Windows x64 programs (./build.sh --win) run under Wine, in an x64 Linux
# container (docker/wine); on Apple Silicon, Docker emulates it.
WIN_IMAGE="bflat-zero-wine:1"

ensure_win_image() {
    if ! command -v docker >/dev/null 2>&1; then
        echo "Error: Docker is required to run Windows programs here (under Wine)." >&2
        exit 1
    fi
    if ! docker image inspect "$WIN_IMAGE" >/dev/null 2>&1; then
        docker build --platform linux/amd64 -t "$WIN_IMAGE" docker/wine
    fi
}

# WebAssembly (./setup.sh --wasm, ./build.sh --wasm, ./run.sh --wasm).
# WASM builds don't use bflat's own compiler: NativeAOT-LLVM compiles to LLVM
# bitcode, the WASI SDK's clang compiles and links it, and wasmtime runs it.
# All three are Linux builds for this machine's architecture (on macOS they run
# in the build container), stored in WASM_TOOLS. These versions are tested;
# NativeAOT-LLVM expects WASI SDK 29, and 34 works with --no-stack-first.
#
# Treat them as a set. wasm/runtime.c implements NativeAOT-LLVM's internal
# runtime functions, whose names and signatures aren't documented anywhere:
# they were read from the compiler's output. A new NativeAOT-LLVM can change
# them. A changed signature fails the link (--fatal-warnings), but a changed
# meaning only shows when the program runs. After changing any version, run
# ./build.sh --wasm and compare each sample's output with the native build.
NAOT_LLVM_VERSION=10.0.0-rc.1.26357.1
WASI_SDK_VERSION=34.0
WASMTIME_VERSION=v50.0.1
WASM_TOOLS=${WASM_TOOLS:-tools/wasm}

case "$HOST_ARCH" in
    x64)   WASM_HOST_NUGET=linux-x64;   WASI_SDK_ARCH=x86_64; WASMTIME_ARCH=x86_64 ;;
    arm64) WASM_HOST_NUGET=linux-arm64; WASI_SDK_ARCH=arm64;  WASMTIME_ARCH=aarch64 ;;
esac
WASM_ILC=$WASM_TOOLS/ilc-$NAOT_LLVM_VERSION/tools/ilc
WASI_SDK=$WASM_TOOLS/wasi-sdk-$WASI_SDK_VERSION-$WASI_SDK_ARCH-linux
WASMTIME=$WASM_TOOLS/wasmtime-$WASMTIME_VERSION-$WASMTIME_ARCH-linux/wasmtime

# DOS (./setup.sh --dos, ./build.sh --dos, ./run.sh --dos): 32-bit programs for
# a 386 and later, run under DOS by the HX DOS extender. C# is compiled to LLVM
# bitcode by NativeAOT-LLVM, as for WebAssembly (only that part of WASM_TOOLS
# is needed), and the bitcode is compiled for a 386 by a clang that has the
# x86 target, which the WASI SDK's doesn't.
#
# DOS_TOOLS     where FreeDOS and the HX runtime are stored. Default: tools/dos
# DOS_CLANG     the clang to use. Default: Xcode's on macOS, `clang` elsewhere
# DOS_EMULATOR  bochs or qemu, for run.sh. Default: bochs if installed, since
#               it refuses instructions a 386 doesn't have; QEMU runs most of them
# DOS_CPU       the processor to emulate. Default: i386 (bochs) or 486 (qemu)
HX_VERSION=2.23
FREEDOS_VERSION=1.4
DOS_TOOLS=${DOS_TOOLS:-tools/dos}
DOS_HX=$DOS_TOOLS/hx-$HX_VERSION
DOS_BOOT=$DOS_TOOLS/freedos-$FREEDOS_VERSION.img
DOS_MOUSE=$DOS_TOOLS/CTMOUSE.EXE   # FreeDOS's mouse driver, taken from its image

# Prints the first clang that can compile for 32-bit x86. (/usr/bin/clang on
# macOS is skipped when the Xcode license hasn't been accepted: it only prints
# a notice. The toolchain's own binary works regardless.)
find_dos_clang() {
    local candidate
    for candidate in "${DOS_CLANG:-}" \
        "$(xcode-select -p 2>/dev/null)/Toolchains/XcodeDefault.xctoolchain/usr/bin/clang" \
        /Library/Developer/CommandLineTools/usr/bin/clang clang; do
        [ -n "$candidate" ] || continue
        if echo 'int x;' | "$candidate" -target i386-pc-windows-gnu -x c -c - -o /dev/null 2>/dev/null; then
            echo "$candidate"
            return 0
        fi
    done
    return 1
}

find_dos_emulator() {
    if [ -n "${DOS_EMULATOR:-}" ]; then
        echo "$DOS_EMULATOR"
    elif command -v bochs >/dev/null 2>&1; then
        echo bochs
    elif command -v qemu-system-i386 >/dev/null 2>&1; then
        echo qemu
    else
        return 1
    fi
}

# Commodore 64 (./setup.sh --c64, ./build.sh --c64, ./run.sh --c64): programs
# for the 6502, an 8-bit processor with 16-bit addresses. C# is compiled to
# LLVM bitcode by NativeAOT-LLVM, as for WebAssembly and DOS (only that part
# of WASM_TOOLS is needed); zero-bitcode rewrites it for 2-byte pointers and
# llvm-mos, an LLVM for the 6502 with a C library for the C64, compiles and
# links it. llvm-mos runs on this machine, not in the build container.
# The programs run in VICE, a Commodore emulator (macOS: brew install vice).
#
# Linux distributions ship VICE without the C64's ROMs (they are copyrighted),
# so on Linux ./setup.sh --c64 takes them from VICE's own source release and
# c64/run.sh points VICE at them.
#
# C64_TOOLS     where llvm-mos (and, on Linux, the ROMs) are stored. Default: tools/c64
# C64_EMULATOR  VICE's C64 emulator. Default: x64sc, or x64 if that's all there is
LLVM_MOS_VERSION=v23.4.0
C64_TOOLS=${C64_TOOLS:-tools/c64}
LLVM_MOS=$C64_TOOLS/llvm-mos-$LLVM_MOS_VERSION
VICE_ROMS_VERSION=3.7.1
C64_ROMS=$C64_TOOLS/vice-roms

find_c64_emulator() {
    local candidate
    for candidate in "${C64_EMULATOR:-}" x64sc x64; do
        [ -n "$candidate" ] || continue
        if command -v "$candidate" >/dev/null 2>&1; then
            command -v "$candidate"
            return 0
        fi
    done
    return 1
}

# UEFI (./build.sh --uefi, ./run.sh --uefi): programs the firmware starts,
# run in QEMU with OVMF, the UEFI firmware from the EDK II project. QEMU ships
# it (Homebrew: share/qemu/edk2-*-code.fd); Linux distributions package it
# separately (Debian/Ubuntu: ovmf for x64, qemu-efi-aarch64 for arm64).
#
# UEFI_FIRMWARE  the firmware file to use. Default: the first one found below.

# $1: x64 or arm64. Prints the firmware's path.
find_uefi_firmware() {
    local candidate prefix=
    if [ -n "${UEFI_FIRMWARE:-}" ]; then
        echo "$UEFI_FIRMWARE"
        return 0
    fi
    command -v brew >/dev/null 2>&1 && prefix=$(brew --prefix 2>/dev/null)
    case "$1" in
        x64)
            for candidate in "$prefix/share/qemu/edk2-x86_64-code.fd" /usr/share/qemu/edk2-x86_64-code.fd \
                /usr/share/OVMF/OVMF_CODE_4M.fd /usr/share/OVMF/OVMF_CODE.fd /usr/share/ovmf/OVMF.fd; do
                if [ -f "$candidate" ]; then echo "$candidate"; return 0; fi
            done ;;
        arm64)
            for candidate in "$prefix/share/qemu/edk2-aarch64-code.fd" /usr/share/qemu/edk2-aarch64-code.fd \
                /usr/share/qemu-efi-aarch64/QEMU_EFI.fd /usr/share/AAVMF/AAVMF_CODE.fd; do
                if [ -f "$candidate" ]; then echo "$candidate"; return 0; fi
            done ;;
    esac
    return 1
}

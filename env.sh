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

# bflat can't target macOS, so on macOS it runs in a Linux container of the
# Mac's own architecture (native speed on Apple Silicon).
IMAGE="bflat-zero-build:2-$HOST_ARCH"

ensure_image() {
    if ! command -v docker >/dev/null 2>&1; then
        echo "Error: Docker is required on macOS (bflat can't target macOS)." >&2
        exit 1
    fi
    if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
        docker build --platform "$DOCKER_PLATFORM" -t "$IMAGE" docker
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

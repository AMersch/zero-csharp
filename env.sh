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
IMAGE="bflat-zero-build:$HOST_ARCH"

ensure_image() {
    if ! command -v docker >/dev/null 2>&1; then
        echo "Error: Docker is required on macOS (bflat can't target macOS)." >&2
        exit 1
    fi
    if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
        docker build --platform "$DOCKER_PLATFORM" -t "$IMAGE" docker
    fi
}

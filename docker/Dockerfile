# Build/run environment for macOS hosts (bflat has no macOS target).
# build.sh mounts the repo at /w and the bflat layout at /bflat. The image is
# built for the Mac's own architecture, so it runs natively on Apple Silicon.
#
# Ubuntu 22.04 because the linker bundled with bflat 10.0.0-rc.1 needs
# libtinfo.so.5, which Ubuntu 24.04 no longer ships. binutils provides strip.
FROM ubuntu:22.04
RUN apt-get update \
 && apt-get install -y --no-install-recommends libtinfo5 binutils \
 && rm -rf /var/lib/apt/lists/*

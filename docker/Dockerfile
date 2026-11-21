# Build/run environment for macOS hosts (bflat is a Linux program).
# build.sh mounts the repo at /w and the bflat layout at /bflat. The image is
# built for the Mac's own architecture, so it runs natively on Apple Silicon.
#
# Ubuntu 22.04 because the linker bundled with bflat 10.0.0-rc.1 needs
# libtinfo.so.5, which Ubuntu 24.04 no longer ships. binutils provides strip;
# the two cross binutils provide an assembler for each target architecture
# (zerocore/native/*.S), whichever one the container itself runs on.
#
# After changing this file, bump the image tag in env.sh so existing images
# get rebuilt.
FROM ubuntu:22.04
RUN apt-get update \
 && apt-get install -y --no-install-recommends libtinfo5 binutils \
    binutils-aarch64-linux-gnu binutils-x86-64-linux-gnu \
 && rm -rf /var/lib/apt/lists/*

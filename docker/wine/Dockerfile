# Runs the Windows x64 builds (bin/win/*.exe) under Wine, for test.sh --win
# and run.sh --win. An x64 image whatever the host: Wine runs x64 Windows code
# only on an x64 processor, so on Apple Silicon Docker emulates it.
# After changing this file, bump WIN_IMAGE's tag in env.sh.
FROM --platform=linux/amd64 ubuntu:22.04
RUN apt-get update \
 && apt-get install -y --no-install-recommends wine64 \
 && rm -rf /var/lib/apt/lists/*
# A Wine prefix made once here, so each run doesn't spend seconds creating one.
ENV WINEDEBUG=-all WINEPREFIX=/wine
ENV PATH=/usr/lib/wine:$PATH
RUN wine64 wineboot --init && wineserver -w

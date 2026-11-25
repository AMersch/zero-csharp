#!/usr/bin/env python3
"""Runs x16emu headless and relays what the program prints.

usage: watch.py TIMEOUT EMULATOR [OPTIONS...]

Everything the program writes to the emulator's port ($9FBB) arrives on the
emulator's standard output as it is, after the line "Testbench mode...". The
exit code arrives as "User debug 1: $xx". This script prints the first and
ends the emulator at the second, with the code as its own exit code (124 if
TIMEOUT seconds pass first)."""
import os
import re
import select
import subprocess
import sys
import time

timeout = float(sys.argv[1])
process = subprocess.Popen(sys.argv[2:], stdin=subprocess.DEVNULL,
                           stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
header = b"Testbench mode...\n"
marker = re.compile(rb"User debug 1: \$([0-9a-fA-F]{2})\n")
seen = b""
written = 0
deadline = time.monotonic() + timeout
code = 124
out = sys.stdout.buffer
try:
    fd = process.stdout.fileno()
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            sys.stderr.write("Error: stopped after %g seconds (X16_TIMEOUT).\n" % timeout)
            break
        if not select.select([fd], [], [], remaining)[0]:
            continue
        chunk = os.read(fd, 65536)
        if not chunk:
            sys.stderr.write("Error: the emulator ended before the program did.\n")
            code = 1
            break
        seen += chunk
        start = len(header) if seen.startswith(header) else 0
        if not seen.startswith(header[:len(seen)]) or len(seen) >= len(header):
            found = marker.search(seen, start)
            end = found.start() if found else len(seen)
            out.write(seen[max(start, written):end])
            out.flush()
            written = max(written, end)
            if found:
                code = int(found.group(1), 16)
                break
finally:
    process.kill()
    process.wait()
sys.exit(code)

#!/usr/bin/env python3
"""Standard input as the cartridge image cx16/runtime.c reads a test's input
from: "ZIN1", the length in two bytes, the text. The emulator's -cartbin takes
it as ROM bank 32; a bank is 16 KB."""
import sys

text = sys.stdin.buffer.read()
if len(text) > 16384 - 6:
    sys.exit("Error: the input is more than 16378 bytes.")
sys.stdout.buffer.write(b"ZIN1" + len(text).to_bytes(2, "little") + text)

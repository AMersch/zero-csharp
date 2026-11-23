#!/usr/bin/env python3
"""Turns standard input into the text of VICE's -keybuf option: what to type
into the emulated C64's keyboard. VICE takes ASCII and converts it to the
C64's character set itself; a newline is written \\n and a backslash \\\\.

A file ends and a keyboard doesn't, so the text is followed by the line that
c64/runtime.c takes as the end of input: the left-arrow key ('_') alone."""
import sys

text = sys.stdin.buffer.read().decode("ascii", "replace")
if text and not text.endswith("\n"):
    text += "\n"
text += "_\n"
sys.stdout.write("".join(
    "\\n" if c == "\n" else "\\\\" if c == "\\" else c
    for c in text if c == "\n" or " " <= c <= "~"))

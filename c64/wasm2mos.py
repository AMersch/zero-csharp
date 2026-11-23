#!/usr/bin/env python3
"""Rewrites a WebAssembly object file as 6502 sources.

NativeAOT-LLVM writes a program's data (type descriptions, string literals,
static fields) and a few small functions as a WebAssembly object file.
dos/wasm2i386.py turns such a file into assembly and C; this is the same
tool for the 6502, where three things differ:

  - An address is 2 bytes. The data keeps its 32-bit layout (see ll2mos.py),
    so each 4-byte slot that holds one gets the address and two zero bytes.
  - A pointer and a 32-bit integer are passed in different registers, and
    WebAssembly calls both i32. The functions' real types are read from the
    program's LLVM IR, where each of them is declared or defined.
  - C's int is 16 bits, but the functions C# imports ([DllImport]) are called
    with C#'s 32-bit ints. So each import is bound to cs_<name>, which
    c64/runtime.c defines with 32-bit parameters: C#'s calloc is cs_calloc
    there, and the C library's own calloc keeps its name and its types.

Usage: wasm2mos.py IN.o OUT MODULE.ll...     writes OUT.s and, if needed, OUT.c
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "dos"))
sys.path.insert(0, HERE)

import wasm2i386  # noqa: E402
from ll2mos import signatures  # noqa: E402

C_TYPES = {"ptr": "void *", "i32": "u32", "i16": "unsigned short", "i8": "unsigned char", "void": "void"}


class Mos(wasm2i386.Target):
    tool = "c64/wasm2mos.py"
    comment = ";"
    prefix = ""
    u32 = "unsigned long"

    def __init__(self, modules):
        self.defined, self.declared = signatures(modules)

    def address(self, expression, section):
        # An import's "cell": the slot compiled code reads the function's
        # address from.
        if section.startswith(".rodata.cell."):
            expression = '"cs_' + expression[1:]
        return f"\t.short {expression}\n\t.short 0"

    def signature(self, name):
        if name in self.defined:
            result, parameters = self.defined[name]
        elif name in self.declared:
            result, parameters = sorted(self.declared[name])[0]
        elif name.startswith("unbox_"):
            # Called only through a method table, so not declared anywhere:
            # the method of a struct, for a boxed one. Same types.
            return self.signature(name[len("unbox_"):])
        else:
            wasm2i386.fail(f"{name} isn't declared in the program's IR, so its types are unknown")
        try:
            return C_TYPES[result], [C_TYPES[p] for p in parameters]
        except KeyError as unknown:
            wasm2i386.fail(f"{name} has a parameter or result of type {unknown}, which isn't supported")


if __name__ == "__main__":
    if len(sys.argv) < 4:
        sys.exit(__doc__)
    with open(sys.argv[1], "rb") as source:
        module = wasm2i386.Module(source.read())
    target = Mos(sys.argv[3:])
    wasm2i386.write_data(module, sys.argv[2] + ".s", target)
    if module.bodies:
        wasm2i386.write_code(module, sys.argv[2] + ".c", target)

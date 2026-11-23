#!/usr/bin/env python3
"""Rewrites a WebAssembly object file as i386 sources.

NativeAOT-LLVM writes a program's code as LLVM bitcode, which clang can compile
for any processor, but its data (type descriptions, string literals, static
fields) as a WebAssembly object file. This turns such a file into:

  OUT.s   the data, as assembly: the same bytes, with each address the
          WebAssembly linker would have filled in written as a symbol reference
  OUT.c   the few functions the file contains, translated to C (they are tiny
          accessors for static fields; anything else is refused)

Usage: wasm2i386.py IN.o OUT            writes OUT.s and, if needed, OUT.c

Symbols get the leading underscore that C symbols have in 32-bit Windows
object files, so the output links with clang's i386-pc-windows-gnu code.

c64/wasm2mos.py uses this file too, with another Target (below).
"""
import sys

# ---- reading -----------------------------------------------------------------


class Reader:
    def __init__(self, data, pos=0, end=None):
        self.d, self.p, self.end = data, pos, len(data) if end is None else end

    def more(self):
        return self.p < self.end

    def byte(self):
        self.p += 1
        return self.d[self.p - 1]

    def uleb(self):
        result = shift = 0
        while True:
            b = self.byte()
            result |= (b & 0x7F) << shift
            shift += 7
            if not b & 0x80:
                return result

    def sleb(self):
        result = shift = 0
        while True:
            b = self.byte()
            result |= (b & 0x7F) << shift
            shift += 7
            if not b & 0x80:
                if b & 0x40:
                    result -= 1 << shift
                return result

    def take(self, n):
        self.p += n
        return self.d[self.p - n:self.p]

    def name(self):
        return self.take(self.uleb()).decode()


def fail(message):
    sys.exit(f"wasm2i386: {message}")


# Relocation types (WebAssembly tool conventions, Linking.md).
R_FUNCTION_INDEX_LEB, R_TABLE_INDEX_SLEB, R_TABLE_INDEX_I32 = 0, 1, 2
R_MEMORY_ADDR_LEB, R_MEMORY_ADDR_SLEB, R_MEMORY_ADDR_I32 = 3, 4, 5
WITH_ADDEND = {R_MEMORY_ADDR_LEB, R_MEMORY_ADDR_SLEB, R_MEMORY_ADDR_I32}

SYM_FUNCTION, SYM_DATA, SYM_GLOBAL, SYM_SECTION = 0, 1, 2, 3
FLAG_BINDING_LOCAL, FLAG_UNDEFINED, FLAG_EXPLICIT_NAME = 0x02, 0x10, 0x40


class Module:
    def __init__(self, data):
        if data[:8] != b"\0asm\1\0\0\0":
            fail("not a WebAssembly object file")
        self.data = data
        self.types = []          # (params, results), each a list of value types
        self.func_imports = []   # (name, type index)
        self.func_types = []     # type index of each function defined here
        self.bodies = []         # (locals, start of code, end, start of body entry)
        self.segments = []       # (offset of the bytes within the DATA section, bytes)
        self.segment_info = []   # (name, alignment as a power of two)
        self.symbols = []
        self.relocs = {}         # target section index -> [(type, offset, symbol, addend)]
        self.section_index = {}  # section id -> index among all sections
        self.code_start = self.data_start = 0

        r = Reader(data, 8)
        index = 0
        while r.more():
            kind = r.byte()
            size = r.uleb()
            body = Reader(data, r.p, r.p + size)
            r.p += size
            if kind == 0:
                name = body.name()
                if name == "linking":
                    self.read_linking(body)
                elif name.startswith("reloc."):
                    self.read_relocs(body)
            else:
                self.section_index[kind] = index
                if kind == 1:
                    self.read_types(body)
                elif kind == 2:
                    self.read_imports(body)
                elif kind == 3:
                    self.func_types = [body.uleb() for _ in range(body.uleb())]
                elif kind == 10:
                    self.read_code(body)
                elif kind == 11:
                    self.read_data(body)
            index += 1

    def read_types(self, r):
        for _ in range(r.uleb()):
            if r.byte() != 0x60:
                fail("unsupported type section entry")
            params = [r.byte() for _ in range(r.uleb())]
            results = [r.byte() for _ in range(r.uleb())]
            self.types.append((params, results))

    def read_imports(self, r):
        for _ in range(r.uleb()):
            r.name()
            field = r.name()
            kind = r.byte()
            if kind == 0:
                self.func_imports.append((field, r.uleb()))
            elif kind == 1:      # table: element type, limits
                r.byte()
                self.limits(r)
            elif kind == 2:      # memory: limits
                self.limits(r)
            elif kind == 3:      # global: type, mutability
                r.byte()
                r.byte()
            else:
                fail(f"unsupported import kind {kind}")

    @staticmethod
    def limits(r):
        flags = r.byte()
        r.uleb()
        if flags & 1:
            r.uleb()

    def read_code(self, r):
        self.code_start = r.p
        for _ in range(r.uleb()):
            entry = r.p
            size = r.uleb()
            end = r.p + size
            local_types = []
            for _ in range(r.uleb()):
                count = r.uleb()
                local_types += [r.byte()] * count
            self.bodies.append((local_types, r.p, end, entry))
            r.p = end

    def read_data(self, r):
        self.data_start = r.p
        for _ in range(r.uleb()):
            flags = r.uleb()
            if flags & 2:
                r.uleb()             # memory index
            if not flags & 1:        # active segment: skip its offset expression
                while r.byte() != 0x0B:
                    pass
            size = r.uleb()
            self.segments.append((r.p - self.data_start, r.take(size)))

    def read_linking(self, r):
        if r.uleb() != 2:
            fail("unsupported linking section version")
        while r.more():
            kind = r.byte()
            sub = Reader(r.d, r.p + 0, None)
            size = sub.uleb()
            sub.end = sub.p + size
            r.p = sub.end
            if kind == 5:    # segment info
                for _ in range(sub.uleb()):
                    name = sub.name()
                    align = sub.uleb()
                    sub.uleb()
                    self.segment_info.append((name, align))
            elif kind == 8:  # symbol table
                for _ in range(sub.uleb()):
                    self.symbols.append(self.read_symbol(sub))

    def read_symbol(self, r):
        kind = r.byte()
        flags = r.uleb()
        undefined = bool(flags & FLAG_UNDEFINED)
        sym = {"kind": kind, "flags": flags, "undefined": undefined, "name": None}
        if kind == SYM_FUNCTION:
            sym["index"] = r.uleb()
            if not undefined or flags & FLAG_EXPLICIT_NAME:
                sym["name"] = r.name()
            else:
                sym["name"] = self.func_imports[sym["index"]][0]
        elif kind == SYM_DATA:
            sym["name"] = r.name()
            if not undefined:
                sym["segment"], sym["offset"], sym["size"] = r.uleb(), r.uleb(), r.uleb()
        elif kind == SYM_SECTION:
            sym["section"] = r.uleb()
        else:
            fail(f"unsupported symbol kind {kind}")
        return sym

    def read_relocs(self, r):
        target = r.uleb()
        entries = self.relocs.setdefault(target, [])
        for _ in range(r.uleb()):
            kind = r.byte()
            offset = r.uleb()
            symbol = r.uleb()
            addend = r.sleb() if kind in WITH_ADDEND else 0
            entries.append((kind, offset, symbol, addend))


# ---- what differs between processors -------------------------------------------


class Target:
    """The 386, in a 32-bit Windows object file."""
    tool = "dos/wasm2i386.py"
    comment = "#"           # starts a comment in assembly
    prefix = "_"            # before every C symbol
    u32 = "unsigned int"    # C's 32-bit integer

    def address(self, expression, section):
        """The assembly for a 4-byte slot holding an address."""
        return f"\t.long {expression}"

    def signature(self, name):
        """A function's real C types, as (result, [parameters]), or None when
        every one of them can be passed as a 32-bit integer, as here."""
        return None


# ---- writing the data --------------------------------------------------------


def asm_symbol(name, target):
    return '"' + target.prefix + name + '"'


def write_data(m, out, target=Target()):
    lines = [f"{target.comment} Generated by {target.tool} from a WebAssembly object file.", ""]
    relocs = sorted(m.relocs.get(m.section_index.get(11, -1), []), key=lambda e: e[1])
    defined = {}  # segment -> [(offset, name, local)]
    for sym in m.symbols:
        if sym["kind"] == SYM_DATA and not sym["undefined"]:
            local = bool(sym["flags"] & FLAG_BINDING_LOCAL)
            defined.setdefault(sym["segment"], []).append((sym["offset"], sym["name"], local))

    for index, (start, data) in enumerate(m.segments):
        name, align = m.segment_info[index] if index < len(m.segment_info) else (".data", 2)
        labels = {}
        for offset, sym, local in defined.get(index, []):
            labels.setdefault(offset, []).append((sym, local))
        mine = {offset - start: (kind, symbol, addend)
                for kind, offset, symbol, addend in relocs if start <= offset < start + len(data)}

        # Everything goes into .data: the startup code writes to some of it
        # (static field storage), and one section keeps the layout simple.
        lines.append(f"{target.comment} segment {index}: {name}")
        lines.append("\t.data")
        lines.append(f"\t.p2align {max(align, 2)}")
        pos = 0
        run = []

        def flush():
            for i in range(0, len(run), 16):
                lines.append("\t.byte " + ",".join(str(b) for b in run[i:i + 16]))
            run.clear()

        while pos <= len(data):
            if pos in labels:
                flush()
                for sym, local in labels[pos]:
                    if not local:
                        lines.append(f"\t.globl {asm_symbol(sym, target)}")
                    lines.append(f"{asm_symbol(sym, target)}:")
            if pos == len(data):
                break
            if pos in mine:
                flush()
                kind, symbol, addend = mine[pos]
                if kind not in (R_MEMORY_ADDR_I32, R_TABLE_INDEX_I32):
                    fail(f"unsupported relocation type {kind} in data")
                if any(pos < o < pos + 4 for o in labels):
                    fail("a symbol starts inside a relocated word")
                expr = asm_symbol(m.symbols[symbol]["name"], target)
                if addend:
                    expr += f"{addend:+d}"
                lines.append(target.address(expr, name))
                pos += 4
            else:
                run.append(data[pos])
                pos += 1
        flush()
        lines.append("")

    with open(out, "w") as f:
        f.write("\n".join(lines))


# ---- translating the functions to C -------------------------------------------

I32 = 0x7F


# Symbol names can hold characters C identifiers can't ("Func_2<Int32>"), so
# the C code uses made-up names and gives the real one as an assembler label.
def c_label(name, target):
    if '"' in name or "\\" in name:
        fail(f"symbol '{name}' can't be written as an assembler label")
    return f'__asm__("{target.prefix}{name}")'


def write_code(m, out, target=Target()):
    section = m.section_index.get(10)
    relocs = {offset: (kind, symbol, addend)
              for kind, offset, symbol, addend in m.relocs.get(section, [])}
    names = {}  # function index -> name
    for sym in m.symbols:
        if sym["kind"] == SYM_FUNCTION:
            names[sym["index"]] = sym["name"]
    imports = len(m.func_imports)

    def signature(index):
        type_index = m.func_imports[index][1] if index < imports else m.func_types[index - imports]
        params, results = m.types[type_index]
        if any(t != I32 for t in params + results) or len(results) > 1:
            fail("only functions of 32-bit integers are supported")
        return len(params), len(results)

    decls, bodies, data_decls = [], [], set()

    # Where a pointer and a 32-bit integer aren't passed the same way
    # (target.signature), the translated code still computes with 32-bit
    # integers, in static functions, and these convert at the edges.
    def to_u32(ctype, expr):
        return f"(u32)(__UINTPTR_TYPE__){expr}" if ctype.endswith("*") else f"(u32){expr}"

    def from_u32(ctype, expr):
        return f"({ctype})(__UINTPTR_TYPE__){expr}" if ctype.endswith("*") else f"({ctype}){expr}"

    def typed_parameters(types):
        return ", ".join(f"{t} a{i}" for i, t in enumerate(types)) or "void"

    def typed_signature(name, count, results):
        typed = target.signature(name)
        if typed is not None and (len(typed[1]) != count or (typed[0] != "void") != bool(results)):
            fail(f"{name} has {count} parameters here and {len(typed[1])} where it is called or defined")
        return typed

    # Declares a function with its real signature, so that calling it and
    # taking its address agree. Returns the name to call it by, its number
    # of parameters, whether it has a result, and the name to take its
    # address by.
    def declare(symbol):
        function = m.symbols[symbol]
        count, results = signature(function["index"])
        name = f"f{function['index']}"
        returns = "u32" if results else "void"
        typed = typed_signature(function["name"], count, results)
        if typed is None:
            decl = (f"{returns} {name}({', '.join(['u32'] * count) or 'void'})"
                    f" {c_label(function['name'], target)};")
            if decl not in decls:
                decls.append(decl)
            return name, count, results, name
        real = f"r{function['index']}"
        decl = f"{typed[0]} {real}({typed_parameters(typed[1])}) {c_label(function['name'], target)};"
        if decl not in decls:
            decls.append(decl)
            parameters = ", ".join(f"u32 a{i}" for i in range(count)) or "void"
            if function["index"] < imports:
                # Defined in the compiled code: a way to call it with integers.
                call = f"{real}({', '.join(from_u32(t, f'a{i}') for i, t in enumerate(typed[1]))})"
                body = f"return {to_u32(typed[0], call)};" if results else f"{call};"
                # (Unused when only the function's address is taken.)
                decls.append(f"__attribute__((unused)) static {returns} {name}({parameters}) {{ {body} }}")
            else:
                decls.append(f"static {returns} {name}({parameters});")
        return name, count, results, real

    def declare_data(symbol):
        name = f"d{symbol}"
        data_decls.add(f"extern char {name}[] {c_label(m.symbols[symbol]['name'], target)};")
        return name

    for number, (local_types, start, end, _) in enumerate(m.bodies):
        index = imports + number
        nparams, nresults = signature(index)
        if any(t != I32 for t in local_types):
            fail("only 32-bit integer locals are supported")
        nlocals = nparams + len(local_types)
        code = []
        stack = []
        temp = 0
        depth = 1
        r = Reader(m.data, start, end)

        def push(expr):
            nonlocal temp
            code.append("    " * depth + f"u32 t{temp} = {expr};")
            stack.append(f"t{temp}")
            temp += 1

        def reloc_at(pos, expected):
            entry = relocs.get(pos - m.code_start)
            if entry is None or entry[0] not in expected:
                fail("expected a relocation in a function body")
            return entry

        while r.more():
            op = r.byte()
            if op == 0x41:                       # i32.const
                at = r.p
                value = r.sleb()
                entry = relocs.get(at - m.code_start)
                if entry is None:
                    push(f"{value & 0xFFFFFFFF}u")
                else:
                    kind, symbol, addend = entry
                    if kind == R_MEMORY_ADDR_SLEB:       # the address of data
                        push(f"(u32){declare_data(symbol)} + {addend & 0xFFFFFFFF}u")
                    elif kind == R_TABLE_INDEX_SLEB:     # the address of a function
                        real = declare(symbol)[3]
                        push(f"(u32)&{real}" if target.signature(m.symbols[symbol]["name"]) is None
                             else f"(u32)(__UINTPTR_TYPE__)&{real}")
                    else:
                        fail(f"unsupported relocation type {kind} in code")
            elif op == 0x20:                     # local.get
                push(f"l{r.uleb()}")
            elif op == 0x21:                     # local.set
                code.append("    " * depth + f"l{r.uleb()} = {stack.pop()};")
            elif op == 0x22:                     # local.tee
                code.append("    " * depth + f"l{r.uleb()} = {stack[-1]};")
            elif op == 0x6A:                     # i32.add
                b, a = stack.pop(), stack.pop()
                push(f"{a} + {b}")
            elif op == 0x28:                     # i32.load
                r.uleb()
                offset = r.uleb()
                push(f"*(u32 *)({stack.pop()} + {offset}u)")
            elif op == 0x04:                     # if (no result)
                if r.byte() != 0x40:
                    fail("only 'if' blocks without a result are supported")
                code.append("    " * depth + f"if ({stack.pop()}) {{")
                depth += 1
            elif op == 0x10:                     # call
                at = r.p
                r.uleb()
                kind, symbol, _ = reloc_at(at, {R_FUNCTION_INDEX_LEB})
                name, count, results, _ = declare(symbol)
                args = [stack.pop() for _ in range(count)][::-1]
                call = f"{name}({', '.join(args)})"
                if results:
                    push(call)
                else:
                    code.append("    " * depth + call + ";")
            elif op == 0x1A:                     # drop
                stack.pop()
            elif op == 0x0B:                     # end
                depth -= 1
                if depth > 0:
                    code.append("    " * depth + "}")
            else:
                fail(f"unsupported instruction 0x{op:02x} in function {names.get(index)}")
        if depth != 0:
            fail("unbalanced blocks in a function body")

        params = ", ".join(f"u32 l{i}" for i in range(nparams)) or "void"
        head = f"{'u32' if nresults else 'void'} f{index}({params})"
        typed = typed_signature(names[index], nparams, nresults)
        if typed is None:
            text = [f"// {names[index]}", f"{head} {c_label(names[index], target)};", head, "{"]
        else:
            text = [f"// {names[index]}", f"static {head};", f"static {head}", "{"]
        text += [f"    u32 l{i} = 0;" for i in range(nparams, nlocals)]
        text += code
        if nresults:
            text.append(f"    return {stack.pop()};")
        text.append("}")
        if typed is not None:
            # The function under its real name and with its real types.
            real = f"{typed[0]} r{index}({typed_parameters(typed[1])})"
            call = f"f{index}({', '.join(to_u32(t, f'a{i}') for i, t in enumerate(typed[1]))})"
            text += ["", f"{real} {c_label(names[index], target)};", real, "{",
                     f"    return {from_u32(typed[0], call)};" if nresults else f"    {call};", "}"]
        bodies.append("\n".join(text))

    with open(out, "w") as f:
        f.write(f"// Generated by {target.tool} from a WebAssembly object file.\n")
        f.write(f"typedef {target.u32} u32;\n\n")
        f.write("\n".join(sorted(data_decls) + decls) + "\n\n" + "\n\n".join(bodies) + "\n")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    with open(sys.argv[1], "rb") as source:
        module = Module(source.read())
    write_data(module, sys.argv[2] + ".s")
    if module.bodies:
        write_code(module, sys.argv[2] + ".c")

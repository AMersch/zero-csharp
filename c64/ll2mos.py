#!/usr/bin/env python3
"""Rewrites NativeAOT-LLVM's 32-bit code (LLVM IR, as text) for the 6502.

NativeAOT-LLVM compiles C# for a machine with 4-byte pointers: every object
layout, field offset and array stride in its output assumes them. The 6502's
pointers are 2 bytes. The compiler can't be told that, so memory keeps the
32-bit layout: a pointer in memory stays a 4-byte slot, holding the 16-bit
address and two zero bytes. Only pointers in registers shrink. In the IR:

  store ptr %p, ...        stores the address widened to 32 bits, so the slot's
                           upper half is always zero (it is read as an integer
                           in places)
  load ptr                 unchanged: little-endian, the low half is the address
  alloca ptr               four bytes
  getelementptr ptr, ...   steps of four bytes
  <{ ptr, i32 }>           pointers inside struct and array types become i32;
                           extractvalue and insertvalue convert on the way.
                           NativeAOT-LLVM only uses packed structs, so nothing
                           else in a layout depends on the target.

Four more things, each explained where it is done: the exception flag is
folded away, every function is marked to be compiled for size,
float-to-integer conversions call the runtime, and functions whose
declaration and definition disagree are refused.

Usage: ll2mos.py OUTDIR IN.ll...     writes OUTDIR/<name of each IN.ll>
"""
import os
import re
import sys


def fail(message):
    sys.exit(f"ll2mos: {message}")


# ---- types --------------------------------------------------------------------
#
# A type is a string ("ptr", "i32", "double") or a tuple:
#   ("struct", packed, [fields])    ("array", count, element)


class Cursor:
    def __init__(self, text, at=0):
        self.text, self.at = text, at

    def skip(self):
        while self.at < len(self.text) and self.text[self.at] == " ":
            self.at += 1

    def peek(self, token):
        self.skip()
        return self.text.startswith(token, self.at)

    def eat(self, token):
        if not self.peek(token):
            raise ValueError(f"expected {token!r} at {self.text[self.at:self.at + 30]!r}")
        self.at += len(token)


SCALAR = re.compile(r"(ptr|i\d+|float|double|void)\b")
ARRAY = re.compile(r"\[\s*(\d+) x ")


def parse_type(c):
    c.skip()
    for opening, closing, packed in (("<{", "}>", True), ("{", "}", False)):
        if c.peek(opening):
            c.eat(opening)
            fields = []
            while not c.peek(closing):
                fields.append(parse_type(c))
                if c.peek(","):
                    c.eat(",")
            c.eat(closing)
            return ("struct", packed, fields)
    m = ARRAY.match(c.text, c.at)
    if m:
        c.at = m.end()
        element = parse_type(c)
        c.eat("]")
        return ("array", int(m.group(1)), element)
    m = SCALAR.match(c.text, c.at)
    if not m:
        raise ValueError(f"unknown type at {c.text[c.at:c.at + 30]!r}")
    c.at = m.end()
    return m.group(1)


def has_pointer(t):
    if isinstance(t, str):
        return t == "ptr"
    if t[0] == "array":
        return has_pointer(t[2])
    inside = any(has_pointer(f) for f in t[2])
    if inside and not t[1]:
        # An unpacked struct is laid out by the target's alignment rules,
        # which differ; a rewrite would have to add the padding.
        fail("a struct that isn't packed holds a pointer; its layout would change")
    return inside


def show(t, inside=False):
    """The type as text, with pointers inside aggregates as i32."""
    if isinstance(t, str):
        return "i32" if inside and t == "ptr" else t
    if t[0] == "array":
        return f"[{t[1]} x {show(t[2], True)}]"
    opening, closing = ("<{", "}>") if t[1] else ("{", "}")
    if not t[2]:
        return opening + closing
    return f"{opening} {', '.join(show(f, True) for f in t[2])} {closing}"


def member(t, indices):
    for i in indices:
        t = t[2][i] if t[0] == "struct" else t[2]
    return t


AGGREGATE = re.compile(r"<\{|\[\d+ x ")


def rewrite_types(line):
    """Rewrites every aggregate type in the line that holds a pointer."""
    out, at = [], 0
    while True:
        m = AGGREGATE.search(line, at)
        if not m:
            out.append(line[at:])
            return "".join(out)
        out.append(line[at:m.start()])
        c = Cursor(line, m.start())
        try:
            t = parse_type(c)
        except ValueError:
            # A constant ("<{ i32 1, i32 2 }>"), not a type. One holding a
            # pointer would need its value converted; none has been seen.
            if "ptr" in line[m.start():]:
                fail(f"a constant aggregate with a pointer: {line.strip()}")
            out.append(line[m.start():m.end()])
            at = m.end()
            continue
        out.append(show(t) if has_pointer(t) else line[m.start():c.at])
        at = c.at


# ---- instructions ---------------------------------------------------------------


def operands(text):
    """Splits at the commas that aren't inside brackets."""
    parts, depth, current = [], 0, []
    for ch in text:
        if ch in "([{<":
            depth += 1
        elif ch in ")]}>":
            depth -= 1
        if ch == "," and depth == 0:
            parts.append("".join(current).strip())
            current = []
        else:
            current.append(ch)
    parts.append("".join(current).strip())
    return parts


class Rewriter:
    STORE = re.compile(r"^(\s*)store (volatile )?ptr (.*)$")
    EXTRACT = re.compile(r"^(\s*)(%[\w.$]+) = extractvalue (.*)$")
    INSERT = re.compile(r"^(\s*)(%[\w.$]+) = insertvalue (.*)$")
    FLAG = re.compile(r"^(\s*%[\w.$]+) = load i32, ptr @RhpExceptionThrown, align 4$")
    # Constructs that would need a rewrite of their own, and don't occur.
    UNSUPPORTED = ("cmpxchg", "atomicrmw", "x ptr>", "byval(", "sret(", "va_arg")

    def __init__(self, text):
        self.temporaries = 0
        # The attribute groups ("attributes #1 = { noinline optnone }") that
        # switch optimization off.
        self.unoptimized = set(re.findall(r"^attributes (#\d+) = \{[^}]*\boptnone\b", text, re.M))

    def temporary(self):
        # Named, so the numbered values around it keep their numbers.
        self.temporaries += 1
        return f"%mos.{self.temporaries}"

    def line(self, line):
        if line.startswith(("target datalayout", "target triple")):
            return []           # the 6502's, from the compiler's command line

        # After every call, compiled code checks a flag that says an exception
        # is on its way up. In zero mode nothing sets it: a throw stops the
        # program (zerocore's RhpThrowEx), and the code that would set it sits
        # behind a check of the flag itself. So the flag reads as zero, and
        # the optimizer removes the checks, the handlers and every store:
        # a few percent of a program, and a test after every call. The flag
        # isn't defined anywhere (c64/runtime.c), so a program this is wrong
        # for fails to link instead of misbehaving.
        m = self.FLAG.match(line)
        if m:
            return [f"{m.group(1)} = add i32 0, 0"]

        m = self.STORE.match(line)
        if m:
            indent, volatile, rest = m.groups()
            parts = operands(rest)      # the value, "ptr %where", "align 4"
            t = self.temporary()
            return [f"{indent}{t} = ptrtoint ptr {parts[0]} to i32",
                    f"{indent}store {volatile or ''}i32 {t}, {', '.join(parts[1:])}"]

        m = self.EXTRACT.match(line)
        if m:
            indent, result, rest = m.groups()
            c = Cursor(rest)
            t = parse_type(c)
            parts = operands(rest[c.at:])     # the aggregate, then indices
            if has_pointer(t) and member(t, [int(i) for i in parts[1:]]) == "ptr":
                number = self.temporary()
                return [f"{indent}{number} = extractvalue {show(t)} {', '.join(parts)}",
                        f"{indent}{result} = inttoptr i32 {number} to ptr"]
            return [rewrite_types(line)]

        m = self.INSERT.match(line)
        if m:
            indent, result, rest = m.groups()
            c = Cursor(rest)
            t = parse_type(c)
            parts = operands(rest[c.at:])     # the aggregate, "T value", indices
            if has_pointer(t) and member(t, [int(i) for i in parts[2:]]) == "ptr":
                if not parts[1].startswith("ptr "):
                    fail(f"unexpected insertvalue: {line.strip()}")
                number = self.temporary()
                return [f"{indent}{number} = ptrtoint {parts[1]} to i32",
                        f"{indent}{result} = insertvalue {show(t)} {parts[0]}, i32 {number}, "
                        + ", ".join(parts[2:])]
            return [rewrite_types(line)]

        if not line.lstrip().startswith(";"):
            for construct in self.UNSUPPORTED:
                if construct in line:
                    fail(f"'{construct}' isn't supported: {line.strip()}")

        # Every function is compiled for size above all: there are 50 KB for
        # the whole program, and this alone makes it an eighth smaller.
        # (Not the few the compiler marked "don't optimize": the two can't
        # be combined.)
        if line.startswith("define ") and line.endswith(" {"):
            group = re.search(r" (#\d+) \{$", line)
            if not group or group.group(1) not in self.unoptimized:
                line = line[:-2] + " minsize optsize {"

        line = re.sub(r"= alloca ptr\b", "= alloca i32", line)
        line = re.sub(r"(getelementptr(?: inbounds| nuw| nusw)* \(?)ptr,", r"\1i32,", line)
        # One thread, so thread-local variables are ordinary ones.
        line = line.replace(" thread_local ", " ")
        # C# converts floating point to integers with saturation (NaN is 0,
        # out of range is the nearest limit), which the 6502 compiler can't
        # generate: c64/runtime.c has the conversions as functions.
        line = re.sub(r"@llvm\.fpto([su])i\.sat\.(i\d+)\.(f\d+)", r"@zero_fpto\1i_sat_\2_\3", line)
        return [rewrite_types(line)]


# ---- signatures -------------------------------------------------------------------
#
# A function declared with one signature in the module that calls it and
# defined with another in the module that has it works on a 32-bit processor
# as long as the sizes agree: a pointer and a 32-bit integer travel the same
# way. On the 6502 they use different registers, so the call silently passes
# garbage. This finds such pairs, and c64/wasm2mos.py asks for signatures here.

FUNCTION = re.compile(r'^(define|declare)\s+(.*?)@("[^"]+"|[\w.$]+)\((.*)$')
ALIAS = re.compile(r'^@("[^"]+"|[\w.$]+) = alias (\S+) \((.*?)\), ptr @')


def argument_types(text):
    """The parameter list's types, from the text after the opening bracket."""
    depth, end = 0, len(text)
    for i, ch in enumerate(text):
        if ch == "(":
            depth += 1
        elif ch == ")":
            if depth == 0:
                end = i
                break
            depth -= 1
    return tuple(p.split()[0] for p in operands(text[:end]) if p)


def signatures(paths):
    """Returns ({name: signature} of definitions, {name: {signatures}} of declarations).
    A signature is (result type, (parameter types...)), as written in the IR."""
    defined, declared = {}, {}
    for path in paths:
        with open(path) as source:
            for line in source:
                m = ALIAS.match(line)
                if m:
                    defined[m.group(1).strip('"')] = (m.group(2), argument_types(m.group(3) + ")"))
                    continue
                m = FUNCTION.match(line)
                if m:
                    kind, before, name, rest = m.groups()
                    signature = (before.split()[-1], argument_types(rest))
                    name = name.strip('"')
                    if kind == "define":
                        defined[name] = signature
                    else:
                        declared.setdefault(name, set()).add(signature)
    return defined, declared


def check_signatures(paths):
    defined, declared = signatures(paths)
    for name, found in sorted(declared.items()):
        for signature in found:
            if name in defined and signature != defined[name]:
                fail(f"{name} is defined as {defined[name]} and called as {signature}: "
                     "on the 6502 the arguments or the result would be lost")


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    out, sources = sys.argv[1], sys.argv[2:]
    check_signatures(sources)
    for path in sources:
        with open(path) as source:
            text = source.read()
        rewriter = Rewriter(text)
        lines = []
        for line in text.split("\n"):
            lines.extend(rewriter.line(line))
        with open(os.path.join(out, os.path.basename(path)), "w") as target:
            target.write("\n".join(lines))


if __name__ == "__main__":
    main()

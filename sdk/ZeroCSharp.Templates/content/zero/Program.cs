// A zero-mode program: no .NET runtime and no garbage collector. Out and In
// write and read without allocating; the name is read into a stack buffer.
using System;

Out.Write("What's your name? ");
Span<char> buffer = stackalloc char[64];
int length = In.ReadLine(buffer);
ReadOnlySpan<char> name = length switch
{
    <= 0 => "world",                        // no input, or an empty line
    _ when length > buffer.Length => buffer, // longer than the buffer: keep what fits
    _ => buffer.Slice(0, length),
};
Out.Write("Hello, ");
Out.Write(name);
Out.Line("!");

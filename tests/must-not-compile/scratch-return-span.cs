// expect: CS8352
// A span from a ScratchScope can't be returned: its memory is reused once
// the Arena.Scratch callback returns.
using System;

public static class Program
{
    static Span<int> Leak(scoped ScratchScope scratch) => scratch.AllocSpan<int>(4);

    public static void Main() { }
}

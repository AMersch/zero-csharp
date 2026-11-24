// expect: CS8352
// A span from the scope can't be handed out through a ref parameter.
using System;

public static class Program
{
    static void Fill(scoped ScratchScope scratch, ref Span<int> result) => result = scratch.AllocSpan<int>(4);

    public static void Main() { }
}

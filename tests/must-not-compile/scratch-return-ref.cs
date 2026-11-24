// expect: CS8352
// Nor can a ref to scratch memory.
using System;

public static class Program
{
    static ref int Leak(scoped ScratchScope scratch) => ref scratch.Alloc<int>();

    public static void Main() { }
}

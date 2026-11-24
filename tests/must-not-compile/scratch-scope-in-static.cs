// expect: CS8345
// The scope is a ref struct, so it can't be kept in a static or a class.
using System;

public static class Program
{
    static ScratchScope s_kept;

    public static void Main() { }
}

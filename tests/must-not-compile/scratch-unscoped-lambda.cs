// expect: CS8986
// Leaving out `scoped` doesn't open a way out: a lambda that could let the
// scope's memory escape no longer matches the delegate.
using System;

public ref struct Holder
{
    public Span<int> Kept;
}

public static class Program
{
    public static void Main()
    {
        var holder = new Holder();
        Arena.Scratch(ref holder, static (scratch, ref state) =>
        {
            state.Kept = scratch.AllocSpan<int>(4);
        });
    }
}

// expect: CS8352
// A list's items can't be put in the state, which outlives the callback.
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
        Arena.Scratch(ref holder, static (scoped ScratchScope scratch, scoped ref Holder state) =>
        {
            ScratchList<int> items = scratch.List<int>();
            items.Add(1);
            state.Kept = items.AsSpan();
        });
    }
}

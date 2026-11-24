// expect: CS8352
// A list from the scope can't be put in the state, which outlives the callback.
using System;

public ref struct Holder
{
    public ScratchList<int> Kept;
}

public static class Program
{
    public static void Main()
    {
        var holder = new Holder();
        Arena.Scratch(ref holder, static (scoped ScratchScope scratch, scoped ref Holder state) =>
        {
            state.Kept = scratch.List<int>();
        });
    }
}

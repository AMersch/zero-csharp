// expect: CS8352
// A ref struct of your own built on a scratch list can't escape either.
using System;

public ref struct Stack
{
    ScratchList<int> _items;
    public Stack(ScratchScope scratch) => _items = scratch.List<int>();
}

public ref struct Holder
{
    public Stack Kept;
}

public static class Program
{
    public static void Main()
    {
        var holder = new Holder();
        Arena.Scratch(ref holder, static (scoped ScratchScope scratch, scoped ref Holder state) =>
        {
            state.Kept = new Stack(scratch);
        });
    }
}

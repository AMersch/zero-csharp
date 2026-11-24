// expect: CS8352
// Nor can the scope itself, to allocate from after the callback.
using System;

public ref struct Box
{
    public ScratchScope Scope;
}

public static class Program
{
    public static void Main()
    {
        var box = new Box();
        Arena.Scratch(ref box, static (scoped ScratchScope scratch, scoped ref Box state) =>
        {
            state.Scope = scratch;
        });
    }
}

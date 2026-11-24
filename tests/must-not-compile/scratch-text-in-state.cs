// expect: CS8352
// Text from the scope can't be put in the state, which outlives the callback.
using System;

public static class Program
{
    public static void Main()
    {
        ReadOnlySpan<char> kept = default;
        Arena.Scratch(ref kept, static (scoped ScratchScope scratch, scoped ref ReadOnlySpan<char> state) =>
        {
            ScratchText text = scratch.Text();
            text.Append($"{42}");
            state = text.AsSpan();
        });
    }
}

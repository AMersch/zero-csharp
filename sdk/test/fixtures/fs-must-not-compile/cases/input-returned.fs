// expect: FS0412
// With an input passed by value, the result still can't be a span.
open System
let leak (values: Span<int>) =
    Arena.Scratch(values, ScratchFuncIn<Span<int>, Span<int>>(fun scratch input ->
        scratch.AllocSpan<int>(input.Length)))

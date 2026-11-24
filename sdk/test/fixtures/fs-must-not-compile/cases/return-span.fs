// expect: FS0412
// A span from the scope can't be the callback's result.
open System
let leak () =
    Arena.Scratch(ScratchFunc<Span<int>>(fun scratch -> scratch.AllocSpan<int>(4)))

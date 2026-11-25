// expect: FS0406
// The callback can't assign a span to a variable of the function around it.
open System
let leak () =
    let mutable outer = Span<int>.Empty
    Arena.Scratch(ScratchAction(fun scratch -> outer <- scratch.AllocSpan<int>(4)))
    outer.Length

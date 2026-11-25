// expect: FS0406
// Nor hand it out through a byref parameter of that function.
open System
let leak (keep: byref<Span<int>>) =
    let target = &keep
    Arena.Scratch(ScratchAction(fun scratch -> target <- scratch.AllocSpan<int>(4)))

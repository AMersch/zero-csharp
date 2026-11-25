// expect: FS0412
// Wrapping the span doesn't get it out: an option can't hold one.
open System
let leak () =
    Arena.Scratch(ScratchFunc<Span<int> option>(fun scratch -> Some(scratch.AllocSpan<int>(4))))

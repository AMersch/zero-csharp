// expect: FS0437
// A class can't keep a span in a static either.
open System
type Kept() =
    static member val Span = Span<int>.Empty with get, set
let leak () =
    Arena.Scratch(ScratchAction(fun scratch -> Kept.Span <- scratch.AllocSpan<int>(4)))

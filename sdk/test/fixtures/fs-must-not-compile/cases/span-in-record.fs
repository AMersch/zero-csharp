// expect: FS0437
// A record (an object, or a struct that isn't a ref struct) can't have a
// span field, so nothing that lives on can hold one.
open System
type Kept = { mutable Span: Span<int> }
let leak (kept: Kept) =
    Arena.Scratch(ScratchAction(fun scratch -> kept.Span <- scratch.AllocSpan<int>(4)))

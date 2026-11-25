// expect: ZERO006
// A state passed by ref can't be a ref struct: F# would let the callback
// store a span from the scope in it. The SDK's check refuses it.
open System
let leak () =
    let mutable kept = Span<int>.Empty
    Arena.Scratch(&kept, ScratchAction<Span<int>>(fun scratch state ->
        state <- scratch.AllocSpan<int>(4)))
    kept.Length

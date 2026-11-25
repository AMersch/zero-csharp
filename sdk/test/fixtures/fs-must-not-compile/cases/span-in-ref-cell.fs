// expect: FS0412
// A ref cell can't hold a span.
open System
let leak () =
    let cell = ref Span<int>.Empty
    Arena.Scratch(ScratchAction(fun scratch -> cell.Value <- scratch.AllocSpan<int>(4)))
    cell.Value.Length

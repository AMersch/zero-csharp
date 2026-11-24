// expect: FS0412
// Nor can an array.
open System
let leak () =
    let kept: Span<int>[] = Array.zeroCreate 1
    Arena.Scratch(ScratchAction(fun scratch -> kept[0] <- scratch.AllocSpan<int>(4)))
    kept[0].Length

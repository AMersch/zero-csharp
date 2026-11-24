// expect: FS0412
// Nor the scope itself, to allocate from after the callback.
let leak () =
    Arena.Scratch(ScratchFunc<ScratchScope>(fun scratch -> scratch))

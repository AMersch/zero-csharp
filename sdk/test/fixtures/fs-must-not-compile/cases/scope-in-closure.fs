// expect: FS0406
// Nor the scope.
let leak () =
    let later = ref (fun () -> 0)
    Arena.Scratch(ScratchAction(fun scratch ->
        later.Value <- fun () -> scratch.AllocSpan<int>(1).Length))
    later.Value ()

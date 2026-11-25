// expect: FS0406
// A closure that outlives the callback can't capture a span from the scope.
let leak () =
    let later = ref (fun () -> 0)
    Arena.Scratch(ScratchAction(fun scratch ->
        let span = scratch.AllocSpan<int>(4)
        later.Value <- fun () -> span.Length))
    later.Value ()

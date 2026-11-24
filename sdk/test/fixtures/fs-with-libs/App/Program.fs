// An F# program using a C# zero library and an F# one.
module Program

[<EntryPoint>]
let main _ =
    let stack = CsLib.IntStack(4)
    stack.Push 20
    stack.Push 22
    Out.Line("C# library: ", int64 (stack.Pop() + stack.Pop()))
    let tree = FsLib.Tree.ofList [ 5; 3; 8; 1 ]
    Out.Line("F# library: ", int64 (FsLib.Tree.sum tree))
    Out.Line(if FsLib.Tree.contains 8 tree then "found 8" else "no 8")
    0

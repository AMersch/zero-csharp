// expect: FS0412
// Nor can a list from the scope.
let leak () =
    Arena.Scratch(ScratchFunc<ScratchList<int>>(fun scratch -> scratch.List<int>()))

// expect: ZERO006
// The same for a ref struct of your own. (Not a record: fsc after 10.0.402
// gives records a ToString that boxes them, which a ref struct can't be.)
open System
[<Struct; System.Runtime.CompilerServices.IsByRefLike>]
type Holder =
    val mutable Kept: Span<int>
let leak () =
    let mutable holder = Holder()
    Arena.Scratch(&holder, ScratchAction<Holder>(fun scratch state ->
        state.Kept <- scratch.AllocSpan<int>(4)))
    holder.Kept.Length

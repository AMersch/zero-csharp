// Memory lifetimes from F#: an arena with lib's collections, ordinary F#
// values (lists, closures, options) in an object scope, and Arena.Scratch.
// The output is the same whatever fsc optimizes (sdk/test/run.sh builds it
// both ways), and DebugChecks stops the program if a static field is left
// pointing into an arena.
open System

Arena.DebugChecks <- true

// docs:arena
// One arena per lifetime: this one holds a list for as long as it lives.
let level = Arena.Create(1024un, "level")
let scores = List<int>.Create(level)
for s in [ 30; 10; 20 ] do
    scores.Add s |> ignore
printfn "%d scores, first %d" scores.Count (scores.Get 0)
// docs:end

// docs:object-scope
// Each round's lists, closures and options go into the frame arena, and
// closing the scope frees them: a thousand rounds leave nothing behind.
let frame = Arena.Create(4096un, "frame")
let round (n: int) =
    use _ = frame.OpenObjectScope()
    let evens =
        [ 1 .. 100 ]
        |> List.map (fun x -> x * 2)
        |> List.filter (fun x -> x % 3 <> 0)
    let rotated =
        match evens with
        | [] -> []
        | first :: rest -> rest @ [ first ]
    let zero = Some n |> Option.map (fun r -> r - n) |> Option.defaultValue 0
    List.sum rotated + zero

let first = round 0
let before = GC.GetTotalAllocatedBytes()
let mutable total = 0
for n in 1 .. 1000 do
    total <- total + round n
let neverFreed = GC.GetTotalAllocatedBytes() - before
printfn "total %d, first %d, never freed: %d bytes" total first neverFreed
// docs:end

// docs:scratch
// Temporary memory F# checks: nothing from the scope can leave the callback.
[<Struct>]
type Stats = { mutable Sum: int; mutable Count: int }

let summarize (values: int[]) =
    let mutable stats = { Sum = 0; Count = 0 }
    let median =
        Arena.Scratch(&stats, ScratchFunc<Stats, int>(fun scratch st ->
            // A copy to sort, so the caller's array keeps its order.
            let copy = scratch.AllocSpan<int>(values.Length)
            Span<int>(values).CopyTo(copy)
            copy.Sort()
            for v in copy do
                st.Sum <- st.Sum + v
            st.Count <- copy.Length
            copy[copy.Length / 2]))
    median, stats

let values = [| 5; 3; 8; 1; 9 |]
let median, stats = summarize values
printfn "median %d, sum %d of %d, first still %d"
    median stats.Sum stats.Count values[0]
// docs:end

// docs:scratch-list
// Lists and text that grow, from the scope; the string to keep is copied
// out into an arena of yours.
let mutable keep = Arena.Create(1024un, "keep")
let report =
    Arena.Scratch(&keep, ScratchFunc<Arena, string>(fun scratch target ->
        let evens = scratch.List<int>(4)
        for i in 18 .. -2 .. 0 do
            evens.Add i
        evens.AsSpan().Sort()
        let text = scratch.Text()
        text.Append(int64 evens.Count)
        text.Append(" evens:".AsSpan())
        for e in evens do
            text.Append(' ')
            text.Append(int64 e)
        text.ToArenaString(target)))
printfn "%s" report
// docs:end

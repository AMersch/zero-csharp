// Memory lifetimes from F#: an arena with lib's collections, ordinary F#
// values (lists, closures, options) in an object scope, and Arena.Scratch
// (callbacks made once, inputs by value, a state by reference).
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
[<Safety "a round returns an int, and keeps nothing it made">]
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
// The callback is made once, as a C# static lambda is, so a call allocates
// nothing. What it works on goes in as the input, by value: here a span.
let medianOf =
    ScratchFuncIn<ReadOnlySpan<int>, int>(fun scratch input ->
        // A copy to sort, so the caller's items keep their order.
        let copy = scratch.AllocSpan<int>(input.Length)
        input.CopyTo(copy)
        copy.Sort()
        copy[copy.Length / 2])

let median (values: ReadOnlySpan<int>) = Arena.Scratch(values, medianOf)

let values = [| 5; 3; 8; 1; 9 |]
// One call first: it sets up what the later calls reuse.
let firstMedian = median (ReadOnlySpan<int>(values))
let scratchBefore = GC.GetTotalAllocatedBytes()
let mutable medians = 0
for _ in 1 .. 1000 do
    medians <- medians + median (ReadOnlySpan<int>(values))
let scratchNeverFreed = GC.GetTotalAllocatedBytes() - scratchBefore
printfn "median %d, first still %d, never freed: %d bytes"
    (medians / 1000 + firstMedian - 5) values[0] scratchNeverFreed
// docs:end

// docs:scratch-fill
// A span of yours that the callback fills: it gets a copy of the span,
// which points at the same items.
let squaresInto =
    ScratchActionIn<Span<int>>(fun scratch output ->
        let work = scratch.List<int>()
        for i in 1 .. output.Length do
            work.Add(i * i)
        work.AsSpan().CopyTo(output))

let squares: int[] = Array.zeroCreate 4
Arena.Scratch(Span<int>(squares), squaresInto)
printfn "squares %d %d %d %d" squares[0] squares[1] squares[2] squares[3]
// docs:end

// docs:scratch-state
// Several results come back through a state passed by reference. It can't
// be a ref struct (ZERO006), so what goes in with it is an array.
[<Struct>]
type Stats =
    { Values: int[]
      mutable Sum: int
      mutable Largest: int }

let statsOf =
    ScratchAction<Stats>(fun scratch st ->
        let sorted = scratch.AllocSpan<int>(st.Values.Length)
        Span<int>(st.Values).CopyTo(sorted)
        sorted.Sort()
        for v in sorted do
            st.Sum <- st.Sum + v
        st.Largest <- sorted[sorted.Length - 1])

let mutable stats = { Values = values; Sum = 0; Largest = 0 }
Arena.Scratch(&stats, statsOf)
printfn "sum %d, largest %d" stats.Sum stats.Largest
// docs:end

// docs:scratch-list
// Lists and text that grow, from the scope; the string to keep is copied
// out into an arena of yours.
let reportOf =
    ScratchFunc<Arena, string>(fun scratch target ->
        let evens = scratch.List<int>(4)
        for i in 18 .. -2 .. 0 do
            evens.Add i
        evens.AsSpan().Sort()
        let text = scratch.Text()
        text.Append(evens.Count)
        text.Append(" evens:")
        for e in evens do
            text.Append(' ')
            text.Append(e)
        text.Append(", half of them ")
        text.Append(float evens.Count / 2.0)
        text.ToArenaString(target))

let mutable keep = Arena.Create(1024un, "keep")
printfn "%s" (Arena.Scratch(&keep, reportOf))
// docs:end

// docs:scratch-inline
// A lambda written at the call is a new delegate each time: 48 bytes that
// are never freed, and more if it captures something. Fine for a call or
// two; in a loop, make the callback once, or free them with an object scope.
let inlineBefore = GC.GetTotalAllocatedBytes()
let mutable lengths = 0
for _ in 1 .. 100 do
    let length = Arena.Scratch(fun scratch -> scratch.AllocSpan<int>(8).Length)
    lengths <- lengths + length
let inlineNeverFreed = GC.GetTotalAllocatedBytes() - inlineBefore
printfn "inline: %d, something never freed: %b" lengths (inlineNeverFreed > 0L)
// docs:end

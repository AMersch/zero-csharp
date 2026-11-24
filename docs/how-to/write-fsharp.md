---
title: Write a program in F#
index: 22
---

# Write a program in F#

A zero project can be written in F#. It's an `.fsproj` with the same SDK, and builds the same kind of
small native program, for the same targets. Records, unions, pattern matching, closures, lists,
`seq { }`, `Map`, `Set`, `decimal` and `printf` work. The program has no .NET runtime and no garbage collector,
the same as a C# zero program.

This guide uses the .NET SDK packages ([Install with dotnet](../get-started/install-with-dotnet.html)),
from version 0.12.0: 0.11.0 and earlier have no F#. Its code comes from the SDK's tests, which
build each program and check what it prints against .NET's F#.

## Make a project

The templates make F# projects with `-lang F#`:

```bash
dotnet new zero -lang F# -o Hello
dotnet new zerolib -lang F# -o Shapes
```

```xml
<Project Sdk="ZeroCSharp.Sdk/0.12.0">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.fs" />
  </ItemGroup>
</Project>
```

Build and run it as a C# zero project: `dotnet build`, `dotnet run`, `dotnet publish`, and
`-p:ZeroTarget=win-x64` (or `linux-arm64`, `osx-arm64`) for another target. The F# compiler is the
one in your .NET SDK; the project compiles against the package's own small FSharp.Core, not the
NuGet one.

## Write the program

Top-level code is the program, as in a script:

<!-- sample: sdk/test/fixtures/fstoplevel/Program.fs file -->
```fsharp no-check reason="a zero program: the SDK tests build and run it"
let greet name = "Hello, " + name + "!"

Out.Line(greet "top-level F#")
```

Or mark a function `[<EntryPoint>]`, which gets the arguments and returns the exit code. Types and
functions from earlier files in the project work as usual:

<!-- sample: sdk/test/fixtures/fsapp/Shapes.fs file -->
```fsharp no-check reason="a file of a zero project: the SDK tests build and run it"
module Shapes

type Shape =
    | Circle of radius: float
    | Rect of width: float * height: float

type Point = { X: int; Y: int }

let area shape =
    match shape with
    | Circle r -> 3.0 * r * r
    | Rect (w, h) -> w * h

let rec sum xs =
    match xs with
    | [] -> 0
    | x :: rest -> x + sum rest

let rec map f xs =
    match xs with
    | [] -> []
    | x :: rest -> f x :: map f rest
```

<!-- sample: sdk/test/fixtures/fsapp/Program.fs file -->
```fsharp no-check reason="a file of a zero project: the SDK tests build and run it"
module Program

open Shapes

[<EntryPoint>]
let main argv =
    Out.Line("args: ", int64 argv.Length)
    let tens = [ 1; 2; 3; 4 ] |> map (fun x -> x * 10)
    Out.Line("sum: ", int64 (sum tens))
    let same = { X = 1; Y = 2 } = { X = 1; Y = 2 }
    Out.Line(if same then "records equal" else "records differ")
    Out.Write "area: "
    Out.Write(area (Rect(2.0, 3.5)))
    Out.Line()
    Out.Line("F# " + "zero")
    7
```

`Out`, `In`, `File` and the rest of the library are there, as in C#. Records and unions compare and
hash by their contents, as in .NET.

## Print with printf

`printf`, `printfn`, `eprintfn`, `sprintf`, `failwithf` and interpolated strings work, with widths,
flags and precision:

<!-- sample: sdk/test/fixtures/fsprintf/Program.fs basics -->
```fsharp
let name = "zero"
let n = 42
printfn "Hello, %s! %d" name n
printfn "%5d|%-5d|%05d|%+d|%x|%X|%o|%B" 42 42 42 42 255 255 8 5
printfn "%d %x %u" -7 -1 3000000000u
printfn "%f %.2f %e %g %g" 3.14159 2.5 12345.678 0.0001 1e20
printfn "%c %b %O %s" 'z' true (box 3) (string 2.5)
let s = sprintf "%s-%i" "a" -7
printfn "%s" s
printfn $"interpolated {n} and %d{n * 2} and {name}"
```

`%A` isn't there: it shows a value by reflection, which zero mode doesn't have, and the compiler
refuses it. `%O` calls `ToString`.

A function that takes a format and passes it on must be `inline`, as printf's own functions are.
The compiler then works out the arguments at each call:

<!-- sample: sdk/test/fixtures/fsprintf/Program.fs wrapper -->
```fsharp no-check reason="uses the zero library: the SDK tests build and run it"
// A wrapper taking the format: inline, as printf's own functions are.
let inline count format =
    Printf.ksprintf (fun (text: string) -> text.Length) format
printfn "length %d" (count "%s%s" "ab" "cd")
let inline log format =
    Printf.ksprintf (fun (text: string) -> Out.Line("log: " + text)) format
log "%d items, %s" 3 "ok"
```

## Use lists, sequences, maps and sets

The `List`, `Array`, `Seq`, `Map`, `Set` and `String` modules have FSharp.Core's functions, with the
same results. Arrays and strings are sequences too:

<!-- sample: sdk/test/fixtures/fscollections/Program.fs inventory -->
```fsharp
type Item = { Name: string; Price: float; Qty: int }

let items =
    [ { Name = "apple"; Price = 0.5; Qty = 10 }
      { Name = "pear"; Price = 0.75; Qty = 4 }
      { Name = "plum"; Price = 0.25; Qty = 0 } ]

let inStock = items |> List.filter (fun i -> i.Qty > 0)
let worth = inStock |> List.sumBy (fun i -> i.Price * float i.Qty)
printfn "%d in stock, worth %.2f" inStock.Length worth

let byName = items |> List.map (fun i -> i.Name, i) |> Map.ofList
printfn "%s" (String.concat ", " (Map.keys byName))
match Map.tryFind "pear" byName with
| Some pear -> printfn "pears: %d" pear.Qty
| None -> printfn "no pears"
```

Sequence expressions work, lazily, with `yield!`, loops, `use` and `try ... finally`. So do list and
array comprehensions, and ranges of any number type and of `char`:

<!-- sample: sdk/test/fixtures/fscollections/Program.fs seq -->
```fsharp
type Resource(name: string) =
    interface System.IDisposable with
        member _.Dispose() = printfn "dispose %s" name

let rec countdown n = seq {
    if n > 0 then
        yield n
        yield! countdown (n - 1) }

let numbers = seq {
    yield 1
    yield! [ 2; 3 ]
    for i in 4 .. 5 do
        yield i
    let mutable i = 6
    while i <= 7 do
        yield i
        i <- i + 1
    use r = new Resource("r")
    try
        yield 8
    finally
        printfn "finally" }
```

## Count money with decimal

`decimal` works as in .NET, with the same results: 28 digits after the point, rounding half to even,
and the scale it keeps (`19.99M * 3M` is `59.97`, and `1.0M = 1.00M`). `%M` prints one, `%.2f`
rounds it:

<!-- sample: sdk/test/fixtures/fsdecimal/Program.fs decimal -->
```fsharp
let price = 19.99M
let total = price * 3M
printfn "%M %.2f %s" total total (string (total / 7M))
printfn "%M %M %M" (1M / 3M) (2M / 3M) (10M / 4M)
printfn "%M %M" (round 2.5M) (round 3.5M)
let amounts = [ 0.1M; 0.2M; 0.3M ]
printfn "%M %b" (List.sum amounts) (List.sum amounts = 0.6M)
printfn "%M" (List.average [ 1M; 2M; 4M ])
```

C# zero programs get the same `decimal`.

## Watch the memory

There's no garbage collector: memory a program allocates is never given back. F# allocates more
than you might think. Each list cell, closure, tuple, option, `seq` enumerator and `Map` update is an
object, and so is every string `sprintf` and `+` make. A program that runs and ends, as a command-line
tool does, can usually ignore this. One that loops for a long time, a game or a server, needs to give
its memory a lifetime. F# gets the same three ways as C#, which
[Memory](../concepts/memory.html) explains in full; this section shows each from F#. `[<Struct>]`
records, unions and tuples aren't objects, so they need none of it.

An arena holds memory for one lifetime and frees it all at once. Lib's `List`, `Dict` and
`StrBuilder` live in the arena you give them:

<!-- sample: sdk/test/fixtures/fsmemory/Program.fs arena -->
```fsharp no-check reason="uses the zero library: the SDK tests build and run it"
// One arena per lifetime: this one holds a list for as long as it lives.
let level = Arena.Create(1024un, "level")
let scores = List<int>.Create(level)
for s in [ 30; 10; 20 ] do
    scores.Add s |> ignore
printfn "%d scores, first %d" scores.Count (scores.Get 0)
```

### Free F# values with a scope

Ordinary F# code, with its lists, closures and options, allocates with `new`. Inside
`OpenObjectScope` those objects go into the arena instead, and leaving the `use` block frees them.
A loop that would leak on every round then leaks nothing:

<!-- sample: sdk/test/fixtures/fsmemory/Program.fs object-scope -->
```fsharp no-check reason="uses the zero library: the SDK tests build and run it"
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
```

That prints `never freed: 0 bytes`. Nothing may keep a reference to a value made inside the scope
once it closes: not a module-level value, not a ref cell or a collection from outside. Here `round`
returns an `int`, which is copied out. What the compiler keeps for you is safe, unlike in C#: fsc
keeps each lambda that captures nothing, and each union case without fields such as `[]`, in a
static field set by a static constructor, and static constructors allocate outside every scope. A
lambda that captures something is a new object each time, in the arena, and nothing outside points at
it. Set `Arena.DebugChecks <- true` while testing: closing a scope then stops the program if a static
field points into the arena.

`OpenObjectScope` is `unsafe` in C#, and F# has no `unsafe` to ask for, so the rule above is yours
to keep without the compiler's reminder.

### Temporary memory F# checks

`Arena.Scratch` runs a callback with a scope to allocate from, and frees what it allocated when the
callback returns. What the callback works on goes in as an input, passed by value:

<!-- sample: sdk/test/fixtures/fsmemory/Program.fs scratch -->
```fsharp no-check reason="uses the zero library: the SDK tests build and run it"
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
```

That prints `never freed: 0 bytes` after its thousand calls. The scope's spans, lists and text are
`ref struct`s, and F#'s rules for them keep everything inside the callback: a closure can't capture
them, a field, a ref cell or a module-level value can't hold them, and the result can't be one.
`sdk/test/fixtures/fs-must-not-compile/` holds one program per way out, and the SDK's tests check
that the build refuses each.

**Make the callback once.** `medianOf` is a module-level value, so its delegate is created when the
program starts, as a C# `static` lambda's is on its first call. A lambda written at the call also
works, and F# picks the delegate type itself, but each call then makes a new delegate that is never
freed:

<!-- sample: sdk/test/fixtures/fsmemory/Program.fs scratch-inline -->
```fsharp no-check reason="uses the zero library: the SDK tests build and run it"
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
```

The input is a copy, so a span passed that way still points at your items, and the callback can
fill them:

<!-- sample: sdk/test/fixtures/fsmemory/Program.fs scratch-fill -->
```fsharp no-check reason="uses the zero library: the SDK tests build and run it"
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
```

Several results come back through a state passed by reference, with `&`. That form needs the
delegate type written out:

<!-- sample: sdk/test/fixtures/fsmemory/Program.fs scratch-state -->
```fsharp no-check reason="uses the zero library: the SDK tests build and run it"
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
```

Here is the one difference from C#. C# lets that state be a `ref struct` holding spans of yours, and
its `scoped` keyword stops the callback from storing scratch memory in it. F# ignores `scoped`, so the
build checks instead: a callback whose by-reference state is a `ref struct` (a `Span`, or a type
with `[<IsByRefLike>]`) is an error, ZERO006, that names the callback. Pass a `ref struct` by value,
as above, where a copy can't carry anything out; a state passed by reference is a struct or a record
of ordinary values.

Lists and text that grow come from the scope too. Text takes strings, chars and numbers; a string to
keep is copied out into an arena of yours:

<!-- sample: sdk/test/fixtures/fsmemory/Program.fs scratch-list -->
```fsharp no-check reason="uses the zero library: the SDK tests build and run it"
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
```

C#'s `text.Append($"...")`, which formats straight into the text, has no F# counterpart: append the
pieces, as here. `sprintf` works, but makes a string of its own first.

## Build for the Commodore 64

An F# project builds for the C64 as a C# one does, with `-p:ZeroTarget=c64`
([Build for the Commodore 64](build-for-c64.html)). `samples/Paddles.fs` is the paddle-and-ball game
from that guide, in F#: 38 KB, 2 KB more than the C# one. Its test match ends the same way, to the
frame, and allocates nothing while it plays. Struct unions do what C# has to write out by hand:

<!-- sample: samples/Paddles.fs unions -->
```fsharp no-check reason="part of samples/Paddles.fs: the SDK tests build it for the C64 and play its test match"
// Who moves a paddle, and how fast.
[<Struct>]
type Controller =
    | Human of humanSpeed: int
    | Computer of computerSpeed: int

// What a frame of play led to. A struct union keeps its case and fields in
// the value itself, so the 50 outcomes a second are never allocated (zero
// mode never frees memory). C# had to write this one out by hand.
[<Struct>]
type Outcome =
    | InPlay
    | HitWall
    | HitPaddle of paddle: Side
    | Scored of by: Side
```

Active patterns say where the ball is. Marked `[<return: Struct>]`, a partial active pattern returns
a `voption` instead of an `option`, an object, so a match on it allocates nothing:

<!-- sample: samples/Paddles.fs step -->
```fsharp no-check reason="part of samples/Paddles.fs: the SDK tests build it for the C64 and play its test match"
let step left right (m: Match) =
    m.LeftTop <- move m.LeftTop left Left m
    m.RightTop <- move m.RightTop right Right m
    m.Ball <- { m.Ball with X = m.Ball.X + m.Ball.Dx; Y = m.Ball.Y + m.Ball.Dy }

    let wall =
        match m.Ball with
        | OffWall ->
            let ball = m.Ball
            m.Ball <- { ball with Y = Court.clampBall ball.Y; Dy = -ball.Dy }
            HitWall
        | _ -> InPlay
    let hit =
        match m with
        | AtPaddle Left -> bounce Left m
        | AtPaddle Right -> bounce Right m
        | _ -> wall
    match m.Ball with
    | PointFor side -> Scored side
    | _ -> hit
```

The C64 has about 50 KB for a program and its memory, and some F# costs more there than it looks:

- **A lambda passed to a library function**, such as `String.iteri (fun i c -> ...)`, is a closure
  class. On the title screen alone that came to 2 KB; a `for` loop costs nothing extra.
- **`string` and `+` on numbers, and `printf`**, bring in number formatting. The game writes its
  report with `Out.Write`, piece by piece.
- **A function defined inside another one** that uses its values is a closure, made when the outer
  one runs. The game's frame loop is a function of its own, so that playing allocates nothing.

## Use libraries

An F# program can reference zero libraries written in C# or F#, with a `<ProjectReference>` or a
package, as in [Create a library](create-a-library.html):

<!-- sample: sdk/test/fixtures/fs-with-libs/App/Program.fs file -->
```fsharp no-check reason="uses zero libraries: the SDK tests build and run it"
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
```

Ordinary NuGet packages are a different matter: they're built against .NET's runtime, which a zero
program doesn't have, and restore refuses them (NU1202), as for C#.

## Build a native library

An F# library with `<NativeLib>Shared</NativeLib>` builds a native library, as a C# one does
([Build a native library](build-a-native-library.html)). Mark each function to export with
`[<UnmanagedCallersOnly>]` and an `EntryPoint`. It must be public and in a module:

<!-- sample: sdk/test/fixtures/fsnativelib/Exports.fs exports -->
```fsharp no-check reason="part of a module: the SDK tests build the library and call it"
[<UnmanagedCallersOnly(EntryPoint = "nl_add")>]
let add (a: int) (b: int) = a + b

[<UnmanagedCallersOnly(EntryPoint = "nl_mul_long")>]
let mulLong (a: int64) (b: int64) = a * b

[<UnmanagedCallersOnly(EntryPoint = "nl_scale")>]
let scale (x: float) (f: float) = x * f

[<UnmanagedCallersOnly(EntryPoint = "nl_sum")>]
let sum (values: nativeptr<int>) (count: int) : int64 =
    let mutable total = 0L
    for i in 0 .. count - 1 do
        total <- total + int64 (NativePtr.get values i)
    total
```

The same types cross the boundary as in C#: numbers, pointers (`nativeptr<'T>`), and structs of
those, which in F# are `[<Struct>]` types with `val mutable` fields:

<!-- sample: sdk/test/fixtures/fsnativelib/Exports.fs point -->
```fsharp
[<Struct>]
type Point =
    val mutable X: int
    val mutable Y: int
```

`bool` and `char` don't cross, and a function the build can't export (a private one, a generic one,
one with a `bool`) is an error, ZERO005, that names it.

## What's missing

- **`%A`**, as above, and anything else that needs reflection.
- **`async`, `task`, `lazy`** and computation expressions other than `seq`.
- **`asin`, `acos`, `atan`, `sinh`, `cosh` and `tanh`.** The other math functions are there.
- **Catching exceptions.** `try ... with` compiles, but a raised exception ends the program with its
  message, as `throw` does in C# zero mode ([Limits](../concepts/limits.html)). `try ... finally` and
  `use` work.
- **`dict`, `HashSet` and .NET's collections.** Use `Map` and `Set`, or the library's `Dict` and
  `List` ([Choose a collection](choose-a-collection.html)).
- **Casting an array to another array type**, such as `string[]` to `obj[]`, ends the program.

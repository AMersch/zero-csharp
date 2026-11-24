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
tool does, can usually ignore this. One that loops for a long time, a game or a server, should
allocate its data before the loop and use arrays and mutable values inside it. Use F#'s
`[<Struct>]` records, unions and tuples, which aren't objects. [Memory](../concepts/memory.html)
explains what zero mode does with memory, and the arenas the library gives C#.

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

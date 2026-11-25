// Computation expressions with builders the program defines, and lazy values:
// each line is what .NET's FSharp.Core prints (expected.txt).

// docs:option-builder
type OptionBuilder() =
    member _.Bind(x, f) = match x with Some v -> f v | None -> None
    member _.Return v = Some v
    member _.ReturnFrom(o: 'T option) = o
    member _.Zero() = Some()

let option = OptionBuilder()

let digit (c: char) =
    if c >= '0' && c <= '9' then Some(int c - int '0') else None

let addDigits a b = option {
    let! x = digit a
    let! y = digit b
    return x + y }
// docs:end

let show (o: int option) = match o with Some v -> "Some " + string v | None -> "None"

// docs:result-builder
// and! needs MergeSources; BindReturn makes `let! ... return` one call.
type ResultBuilder() =
    member _.Bind(r, f) = match r with Ok v -> f v | Error e -> Error e
    member _.BindReturn(r, f) = match r with Ok v -> Ok(f v) | Error e -> Error e
    member _.MergeSources(a, b) =
        match a, b with
        | Ok x, Ok y -> Ok(x, y)
        | Error e, _ | _, Error e -> Error e
    member _.Return v = Ok v

let result = ResultBuilder()

let positive name n = if n > 0 then Ok n else Error(name + " must be positive")

let area w h = result {
    let! w = positive "width" w
    and! h = positive "height" h
    return w * h }
// docs:end

let showResult (r: Result<int, string>) = match r with Ok v -> "Ok " + string v | Error e -> "Error " + e

// docs:log-builder
// Every control-flow form a builder can support: for, while, use,
// try/finally, if without else, and sequencing.
type Log = { Lines: string list }

type LogBuilder() =
    member _.Yield(line: string) = { Lines = [ line ] }
    member _.Zero() = { Lines = [] }
    member _.Combine(a: Log, b: unit -> Log) = { Lines = a.Lines @ (b ()).Lines }
    member _.Delay(f: unit -> Log) = f
    member _.Run(f: unit -> Log) = f ()
    member _.For(items: seq<'T>, body: 'T -> Log) =
        let mutable lines = []
        for x in items do
            lines <- lines @ (body x).Lines
        { Lines = lines }
    member _.While(guard: unit -> bool, body: unit -> Log) =
        let mutable lines = []
        while guard () do
            lines <- lines @ (body ()).Lines
        { Lines = lines }
    member _.TryFinally(body: unit -> Log, after: unit -> unit) =
        try body () finally after ()
    member this.Using(resource: #System.IDisposable, body: _ -> Log) =
        this.TryFinally((fun () -> body resource), fun () -> resource.Dispose())

let log = LogBuilder()

type Resource(name: string) =
    interface System.IDisposable with
        member _.Dispose() = printfn "dispose %s" name

let steps n = log {
    yield "start"
    for i in 1 .. n do
        yield "step " + string i
    let mutable left = 2
    while left > 0 do
        yield "left " + string left
        left <- left - 1
    use _r = new Resource("r")
    try
        yield "inside"
    finally
        printfn "finally"
    if n > 2 then
        yield "many" }
// docs:end

// A custom operation, as query expressions use them.
type CounterBuilder() =
    member _.Yield(()) = 0
    [<CustomOperation("add")>]
    member _.Add(total: int, n: int) = total + n
    [<CustomOperation("twice")>]
    member _.Twice(total: int) = total * 2

let counter = CounterBuilder()

// docs:lazy
let mutable made = 0

let expensive = lazy (
    made <- made + 1
    printfn "computing"
    6 * 7)

let useExpensive () =
    printfn "created: %b" expensive.IsValueCreated
    printfn "value: %d" (expensive.Force())
    printfn "again: %d, made %d time(s)" expensive.Value made
    printfn "created: %b" expensive.IsValueCreated
// docs:end

[<EntryPoint>]
let main _ =
    printfn "option: %s %s" (show (addDigits '3' '4')) (show (addDigits '3' 'x'))
    printfn "result: %s; %s; %s" (showResult (area 3 4)) (showResult (area 0 4)) (showResult (area 3 -1))
    let lines = (steps 3).Lines
    printfn "log: %s" (String.concat ", " lines)
    printfn "counter: %d" (counter { add 5; add 2; twice })

    useExpensive ()
    let ready = Lazy<string>.CreateFromValue "ready"
    printfn "from value: %s, created %b" ready.Value ready.IsValueCreated
    match lazy (1 + 2) with
    | Lazy 3 -> printfn "pattern: 3"
    | Lazy n -> printfn "pattern: %d" n
    let chain = lazy (expensive.Value + 1)
    printfn "chain: %d" (chain.Force())
    0

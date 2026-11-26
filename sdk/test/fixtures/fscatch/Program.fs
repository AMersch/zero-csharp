// Catch.result and Catch.Run (lib/Catch.cs) from F#: a raise comes back as a
// value, and files opened with use are closed after it.
open System
open System.IO

let mutable failed = 0

let check ok (what: string) =
    if ok then printfn "ok   %s" what
    else
        printfn "FAIL %s" what
        failed <- failed + 1

let rec deep n : int = if n = 0 then failwith "deep" else deep (n - 1) + 1

// Copies a file and returns its size.
let download (url: string) (path: string) : int64 =
    use source = new FileStream(url, FileMode.Open, FileAccess.Read)
    use target = new FileStream(path, FileMode.Create)
    source.CopyTo target
    target.Length

let report url path =
    // docs:result
    match Catch.result (fun () -> download url path) with
    | Ok size -> printfn "%d bytes" size
    | Error (:? IOException as e) -> printfn "couldn't: %s" e.Message
    | Error e -> raise e
    // docs:end

check (Catch.result (fun () -> 6 * 7) = Ok 42) "Catch.result: Ok with the result"

match Catch.result (fun () -> deep 3) with
| Error e -> check (e.Message = "deep") "Catch.result: Error with what failwith raised"
| Ok _ -> check false "Catch.result: Error with what failwith raised"

let e = Catch.Run(fun () -> raise (InvalidOperationException "direct"))
check (not (isNull e) && e.Message = "direct") "Catch.Run: the exception"
check (isNull (Catch.Run(fun () -> ()))) "Catch.Run: null when nothing is raised"

let mutable kept: FileStream = null
let halfway: Result<unit, exn> =
    Catch.result (fun () ->
        use f = new FileStream("fscatch.tmp", FileMode.Create, FileAccess.Write)
        kept <- f
        f.Write([| 1uy; 2uy |], 0, 2)
        f.Flush() // what's still buffered at a raise is lost
        failwith "halfway")
check ((match halfway with Error _ -> true | Ok _ -> false) && not kept.CanWrite)
    "a file opened with use is closed after a raise"

report "fscatch.tmp" "fscatch-copy.tmp"
report "no-such-file" "fscatch-copy.tmp"

let outer =
    Catch.result (fun () ->
        match Catch.result (fun () -> failwith "inner") with
        | Error e -> raise e
        | Ok () -> ())
check (match outer with Error e -> e.Message = "inner" | Ok _ -> false) "raised again, it goes to the outer one"

File.Delete "fscatch.tmp" |> ignore
File.Delete "fscatch-copy.tmp" |> ignore
printfn "%s" (if failed = 0 then "All checks passed." else sprintf "%d checks failed." failed)
if failed <> 0 then Environment.Exit failed

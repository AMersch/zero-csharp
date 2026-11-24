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

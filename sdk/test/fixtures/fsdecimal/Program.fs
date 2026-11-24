// decimal and the math functions: each line is what .NET's F# prints
// (expected.txt).
let show (items: seq<'T>) = "[" + String.concat "; " (Seq.map string items) + "]"

// docs:decimal
let price = 19.99M
let total = price * 3M
printfn "%M %.2f %s" total total (string (total / 7M))
printfn "%M %M %M" (1M / 3M) (2M / 3M) (10M / 4M)
printfn "%M %M" (round 2.5M) (round 3.5M)
let amounts = [ 0.1M; 0.2M; 0.3M ]
printfn "%M %b" (List.sum amounts) (List.sum amounts = 0.6M)
printfn "%M" (List.average [ 1M; 2M; 4M ])
// docs:end

printfn "%M %M %M %M" (floor -1.5M) (ceil -1.5M) (truncate -1.5M) (abs -2.25M)
printfn "%d %d %d" (sign -3.5M) (sign 0M) (sign 7M)
printfn "%M %M %M" (decimal 3) (decimal 2.5) (decimal "1.250")
printfn "%d %g %d" (int 3.7M) (float 0.1M) (int64 -12.9M)
printfn "%M %M" (max 1.5M 1.50M) (min -1M 2M)
printfn "%b %b %b" (1.0M = 1.00M) (1.5M < 1.51M) (compare 2M 10M < 0)
printfn "%s" (show (List.sort [ 3.5M; -1M; 2.25M; 0M ]))
printfn "%M" (pown 1.1M 3)
printfn "%M %M" (79228162514264337593543950335M / 2M) (0.0000000000000000000000000001M * 3M)
printfn "%M" (Seq.sumBy (fun (q, p) -> decimal q * p) [ (2, 1.25M); (3, 0.5M) ])
let byPrice = Map.ofList [ (2.50M, "pear"); (0.99M, "apple"); (10M, "melon") ]
printfn "%s" (String.concat ", " (Map.values byPrice))
printfn "%d %b" (hash 1.0M - hash 1.00M) (1.10M.Equals(1.1M))
printfn "%.3f|%10.1f|%-8.2f|%+.0f" 2.0005M 123.45M 1.005M 2.5M
printfn "%M|%8M|%-8M|%+M" -0.5M 1.25M 1.25M 3M

// The math functions
printfn "%.4f %.4f %.4f" (sqrt 2.0) (exp 1.0) (log 10.0)
printfn "%.4f %.4f %.4f %.4f" (sin 1.0) (cos 1.0) (tan 1.0) (log10 1000.0)
printfn "%.4f" (atan2 1.0 1.0)
printfn "%g %g %g %g" (round 2.5) (round -2.5) (floor -1.5) (ceil 1.2)
printfn "%g %d %d" (truncate -2.7) (sign -4.0) (sign 0)
printfn "%d %d %g" (pown 2 10) (pown 3L 3 |> int) (pown 2.0 -2)
printfn "%g %g" (sqrt 16.0f |> float) (round 1.5f |> float)
printfn "%d %d %d %d" (Checked.(+) 2 3) (Checked.(*) 4 5) (Checked.int 3.7M) (Checked.(-) 10L 4L)

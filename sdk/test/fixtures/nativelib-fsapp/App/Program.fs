// docs:fsharp-call
let mutable point = Point()
point.X <- 1
point.Y <- 12
let moved = MathLib.Offset(point, 10)
printfn "add %d" (MathLib.Add(2, 3))
printfn "offset %d,%d" moved.X moved.Y
// docs:end

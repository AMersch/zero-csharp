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

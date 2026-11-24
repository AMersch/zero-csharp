namespace FsLib

type Tree =
    | Leaf
    | Node of Tree * int * Tree

module Tree =
    let rec insert x tree =
        match tree with
        | Leaf -> Node(Leaf, x, Leaf)
        | Node(l, v, r) when x < v -> Node(insert x l, v, r)
        | Node(l, v, r) -> Node(l, v, insert x r)

    let ofList xs =
        let rec go acc xs =
            match xs with
            | [] -> acc
            | x :: rest -> go (insert x acc) rest
        go Leaf xs

    let rec sum tree =
        match tree with
        | Leaf -> 0
        | Node(l, v, r) -> sum l + v + sum r

    let rec contains x tree =
        match tree with
        | Leaf -> false
        | Node(l, v, r) -> if x = v then true elif x < v then contains x l else contains x r

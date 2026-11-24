---
title: Choose a collection
index: 3
---

# Choose a collection

zerocore has no collections of its own. `lib/`'s are all built on an arena: most hold unmanaged
values, and three hold references. Which one you need depends on what you store and how you find it.

## Pick by what you store and how you look it up

| You store | You find it by | Use | Notes |
|---|---|---|---|
| numbers, structs (unmanaged) | position | `List<T>` | `Add`, `Get`, `Set`, `RemoveAt`, `Sort`, `AsSpan` |
| strings, objects, arrays | position | `RefList<T>` | same API as `List`, plus `foreach` |
| unmanaged values | an unmanaged key | `Dict<TKey, TValue>` | keys compared as .NET's `Dictionary` does |
| unmanaged values | whether it's there | `HashSet<T>` | `Add`, `Contains`, `UnionWith`, `IntersectWith`, `ExceptWith` |
| unmanaged values | first in, first out | `Queue<T>` | `Enqueue`, `TryDequeue`, `TryPeek` |
| unmanaged values | last in, first out | `Stack<T>` | `Push`, `TryPop`, `TryPeek` |
| unmanaged values | smallest priority | `PriorityQueue<TElement, TPriority>` | a binary heap, as .NET's |
| any value | a string's text | `StrDict<TValue>` | keys copied into the map's arena |
| strings, objects, arrays | an unmanaged key | `RefDict<TKey, TValue>` | values stored as references |
| structs that point at each other | following a link | `Ptr<T>` from `arena.New` | linked lists, trees, graphs; see below |

`List<T>` and `Dict` take only unmanaged types because they keep items in arena memory through a `T*`.
A reference in zero mode is just an address (no garbage collector moves or tracks objects), so
`RefList` and `RefDict` store each one as an `nint` in a `List` or `Dict` underneath.

## Create it in an arena

Every collection is created in the arena you pass, and grows there:

<!-- sample: samples/Collections.cs dict -->
```csharp
var scores = Dict<int, int>.Create(arena);
scores.Set(1, 100);
scores.Set(2, 250);
scores.Set(3, 175);
scores.Set(-42, 999);
```

The same rules hold for all five:

- **A handle.** A collection is one pointer to a header in its arena. `var b = a` shares the contents,
  and so does passing it to a method: the callee's `Add`s are visible to the caller.
- **`default` is "not created".** Reads see an empty collection, lookups miss, and `Add` or `Set`
  return false. `IsCreated` tells you which you have.
- **Out of memory returns false.** `Add` and `Set` return false when the arena is full, unless the
  arena was created with `stopOnOutOfMemory: true`.
- **Out of range stops the program.** `Get` and `Set` on a `List` or `RefList` with a bad index end it
  with `List: index out of range`. `RemoveAt` returns false instead.
- **Freed with the arena.** `Reset`, `Restore` or `Destroy` on the arena frees the collection.

## Link items to each other

A struct can't contain itself, so a linked list, a tree or a graph needs pointers between its items.
`arena.New(value)` copies a struct into the arena and returns a `Ptr<T>` to it, and a `Ptr` can be a
field of the struct it points at:

<!-- sample: samples/Links.cs node -->
```csharp
// One item of a doubly linked list: a struct can't contain itself, but it
// can hold pointers to others of its kind.
public struct Node
{
    public int Number;
    public Ptr<Node> Prev;
    public Ptr<Node> Next;
}
```

`ptr.Value` is a `ref` to the item, to read or change in place, and no `unsafe` is needed. A
doubly linked list is then the usual code, cycles included:

<!-- sample: samples/Links.cs chain -->
```csharp
// The list itself: its two ends, and the arena its items live in.
public struct Chain
{
    public Arena Arena;
    public Ptr<Node> First;
    public Ptr<Node> Last;

    public void AddLast(int number)
    {
        Ptr<Node> node = Arena.New(new Node { Number = number, Prev = Last });
        if (Last.IsNull)
            First = node;
        else
            Last.Value.Next = node;
        Last = node;
    }

    // Takes the node out by joining its neighbours to each other.
    public void Remove(Ptr<Node> node)
    {
        ref Node n = ref node.Value;
        if (n.Prev.IsNull)
            First = n.Next;
        else
            n.Prev.Value.Next = n.Next;
        if (n.Next.IsNull)
            Last = n.Prev;
        else
            n.Next.Value.Prev = n.Prev;
        n.Prev = default;
        n.Next = default;
    }
}
```

Recursion works as it does with classes:

<!-- sample: samples/Links.cs insert -->
```csharp
// Recursion works as it would with classes.
static Ptr<TreeNode> Insert(Arena arena, Ptr<TreeNode> node, int key)
{
    if (node.IsNull)
        return arena.New(new TreeNode { Key = key });
    ref TreeNode n = ref node.Value;
    if (key < n.Key)
        n.Left = Insert(arena, n.Left, key);
    else
        n.Right = Insert(arena, n.Right, key);
    return node;
}
```

- **`default` is null.** `IsNull` tells you; `Value` on a null `Ptr` stops the program with
  `Ptr: Value of a null Ptr` instead of crashing it.
- **`==` compares the pointers,** so a walk round a ring can tell when it is back where it started.
- **Items live as long as the arena.** There is no freeing one item: removing a node from a list
  unlinks it, and its memory goes with the arena's.
- **The arena's rules apply.** Like a collection, a `Ptr` is valid until its arena is reset,
  destroyed or restored to before the item was made. Under `Arena.DebugChecks`, a `Ptr` used after
  that stops the program. A `ref` from `Value` kept in a local isn't checked, so take `Value` again
  after anything that frees memory.

## Key by an id, not by an object

`Dict` and `RefDict` keys must be unmanaged, and zerocore's `object` has no `Equals` or `GetHashCode`,
so an object can't be a key. Give the class an `int Id` and key by that: `RefDict<int, T>` to find
the object from its id, `Dict<int, TValue>` for data you keep about it.

<!-- sample: samples/Collections.cs refdict -->
```csharp
var byId = RefDict<int, string>.Create(arena);
byId.Set(7, "seven");
byId.Set(3, Str.Format(arena, $"three is {3}"));
byId.Set(9, null);
```

Hand out each id once and never reuse it. A map entry left behind for an object that is gone would
otherwise answer for the new object with the same id.

## Compare keys your way

`Dict` and `HashSet` compare keys as .NET's `Dictionary` and `HashSet` do, with
`EqualityComparer<T>.Default`: a type's own `Equals(T)` if it implements `IEquatable<T>` (every
primitive does, so `0.0` and `-0.0` are one key), an enum's value, and otherwise a struct's bytes.
None of it boxes. A struct that overrides `Equals(object)` without implementing `IEquatable<T>` is
still compared by its bytes: implement `IEquatable<T>` to have your `Equals` used.

For other rules, name a comparer: `Dict<TKey, TValue, TComparer>` and `HashSet<T, TComparer>` take an
unmanaged struct that implements `IEqualityComparer<T>`. It is kept with the collection, so it can
carry state, and its methods are called directly. `ByteEqualityComparer<T>` compares raw bytes:

<!-- sample: samples/Collections.cs modulo -->
```csharp
// A comparer with state: keys that leave the same remainder are one key.
public struct Modulo : IEqualityComparer<int>
{
    public int By;
    public bool Equals(int a, int b) => a % By == b % By;
    public int GetHashCode(int value) => value % By;
}
```

<!-- sample: samples/Collections.cs dict-comparer -->
```csharp
// As in .NET, 0.0 and -0.0 are one key; by raw bytes, they're two.
var byValue = Dict<double, int>.Create(more);
var byBytes =
    Dict<double, int, ByteEqualityComparer<double>>.Create(more);
byBytes.Set(0.0, 1);
byBytes.Set(-0.0, 2);
byValue.Set(0.0, 1);
byValue.Set(-0.0, 2);
Out.Line($"By bytes: {byBytes.Count} keys; by value: {byValue.Count}");

var lastDigit =
    Dict<int, int, Modulo>.Create(more, comparer: new Modulo { By = 10 });
lastDigit.Set(13, 1);
lastDigit.Set(23, 2);  // the same key as 13
lastDigit.TryGet(3, out int atThree);
Out.Line($"Modulo 10: {lastDigit.Count} key, [3] = {atThree}");
```

A `HashSet` is a `Dict` without values. `Add` returns false for an item that is already there:

<!-- sample: samples/Collections.cs hashset -->
```csharp
var seen = HashSet<int>.Create(more);
int duplicates = 0;
foreach (int id in new[] { 3, 1, 4, 1, 5, 9, 2, 6, 5, 3, 5 })
{
    if (!seen.Add(id))
        duplicates++;
}
Out.Line($"{seen.Count} distinct, {duplicates} duplicates");

var odds = HashSet<int>.Create(more);
for (int i = 1; i < 10; i += 2)
    odds.Add(i);
var both = seen.Clone(more);
both.IntersectWith(odds);      // in seen and odds: 1, 3, 5, 9
var evens = seen.Clone(more);
evens.ExceptWith(odds);        // in seen, not odds: 2, 4, 6
```

## Queues, stacks and priorities

`Queue<T>`, `Stack<T>` and `PriorityQueue<TElement, TPriority>` have .NET's names, with `Try` in front
of the methods that take an item out, since there is no exception to say the collection is empty.
A queue reuses the memory of items already dequeued, so one that is fed as fast as it is drained
doesn't keep growing:

<!-- sample: samples/Collections.cs queue -->
```csharp
var queue = Queue<int>.Create(more);
for (int i = 1; i <= 40; i++)
    queue.Enqueue(i);
int expected = 1;
bool inOrder = true;
for (int i = 0; i < 30; i++)   // past half: the rest move to the front
{
    queue.TryDequeue(out int item);
    inOrder &= item == expected++;
}
for (int i = 41; i <= 50; i++)
    queue.Enqueue(i);
queue.TryPeek(out int front);
while (queue.TryDequeue(out int item))
    inOrder &= item == expected++;
```

A `PriorityQueue` gives the smallest priority first, by its `IComparable<T>`, or by a comparer struct
named as a third type argument (`PriorityQueue<TElement, TPriority, TComparer>`). Elements with equal
priorities come out in no particular order:

<!-- sample: samples/Collections.cs priority-queue -->
```csharp
var tasks = PriorityQueue<char, int>.Create(more);
tasks.Enqueue('c', 3);
tasks.Enqueue('a', 1);
tasks.Enqueue('d', 4);
tasks.Enqueue('b', 2);
Out.Write("By priority:");
while (tasks.TryDequeue(out char task, out int priority))
    Out.Write($" {task}{priority}");
Out.Line();
```

## Sort and iterate

- `Sort()` on a `List`, a `RefList` or a span orders the items by their `IComparable<T>`, as
  `Comparer<T>.Default` does: numbers by value, strings by character code (capitals first).
- `Sort(comparer)` takes anything that implements `IComparer<T>`. Make it a struct and the sort is
  compiled for it, with a direct call to its `Compare`. A lambda (on a span) or the address of a
  static method (`list.Sort(&Compare)`) works too, at the cost of an indirect call per comparison.
- All of them are a heapsort: no extra memory, and not stable. On a sorted span, `BinarySearch`
  finds an item, or the bitwise complement of where it would go.
- `foreach` works on `RefList`, `Dict`, `StrDict` and `RefDict`. Maps yield their pairs in no
  particular order. Don't `Set` or `Remove` during the loop: entries move, so some may be skipped or
  seen twice. For a `List`, loop over `Count` with `Get`, or over `AsSpan()`.

<!-- sample: samples/Collections.cs descending -->
```csharp
// A comparer as a struct: Sort is compiled for it; Compare is a direct call.
public struct Descending : IComparer<int>
{
    public int Compare(int a, int b) => b.CompareTo(a);
}
```

<!-- sample: samples/Collections.cs sort-comparers -->
```csharp
unsorted.Sort();                  // by int's IComparable<int>
Out.Write("Sort():     ");
PrintList(unsorted);
unsorted.Sort(new Descending());  // by a struct: an IComparer<int>
Out.Write("Descending: ");
PrintList(unsorted);

Span<int> span = values;          // spans sort in place too
span.Sort();
Out.Line($"BinarySearch(19): {span.BinarySearch(19)}, "
    + $"(20) would go at {~span.BinarySearch(20)}");
```

## Copy into another arena

`var b = a` doesn't copy. `Clone(target)` makes an independent copy, in the same arena or another one.
Use it to keep a result past the life of a scratch arena:

<!-- sample: samples/Collections.cs clone -->
```csharp
Arena keep = Arena.Create(4096);
StrDict<int> countsCopy = counts.Clone(keep); // keys are copied into keep too
List<int> snapshot = numbers.Clone(keep);
Dict<int, int> scoresCopy = scores.Clone(keep);
```

After this, the sample changes the originals, destroys their arena, and keeps using the clones. A
cloned `List` has just enough capacity for its items.

What a clone copies:

| Collection | Copied into the target arena | Shared with the original |
|---|---|---|
| `List`, `Dict` | the items, byte for byte | whatever a pointer inside an item points at |
| `StrDict` | the entries and the key strings | reference values |
| `RefList`, `RefDict` | the references | the objects themselves |

So a `RefList<string>` clone still points at the original strings. Copy them too, with
`Str.ToArenaString(target, s)`, if their arena goes away.

## Watch out for

- **Pointers into a list move.** `GetPtr` and `AsSpan` are invalidated when the list grows, by
  `RemoveAt`, and by resetting the arena.
- **Growth leaves old buffers behind.** A collection that grows extends its buffer in place while it is
  the arena's newest allocation; otherwise the old buffer stays until the arena is reset. A capacity
  passed to `Create` avoids that.
- **Don't grow an older collection inside a `Save`/`Restore` or a scope.** Its new buffer is freed by
  the `Restore` while the collection still points at it. `Arena.DebugChecks` catches that; see
  [Catch arena mistakes](catch-arena-mistakes.html).

See also: [Memory and arenas](../concepts/memory.html), and the reference pages for
[`List`](../reference/list.html), [`Dict`](../reference/dict.html),
[`HashSet`](../reference/hashset.html), [`Queue`, `Stack` and `PriorityQueue`](../reference/queue.html),
[`RefList`](../reference/reflist.html), [`StrDict`](../reference/strdict.html) and
[`RefDict`](../reference/refdict.html).

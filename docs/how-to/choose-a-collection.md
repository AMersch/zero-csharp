---
title: Choose a collection
index: 3
---

# Choose a collection

zerocore has no collections of its own. `lib/` has five, all built on an arena: two for unmanaged
values and three for references. Which one you need depends on what you store and how you find it.

## Pick by what you store and how you look it up

| You store | You find it by | Use | Notes |
|---|---|---|---|
| numbers, structs (unmanaged) | position | `List<T>` | `Add`, `Get`, `Set`, `RemoveAt`, `Sort`, `AsSpan` |
| strings, objects, arrays | position | `RefList<T>` | same API as `List`, plus `foreach` |
| unmanaged values | an unmanaged key | `Dict<TKey, TValue>` | keys compared by their raw bytes |
| any value | a string's text | `StrDict<TValue>` | keys copied into the map's arena |
| strings, objects, arrays | an unmanaged key | `RefDict<TKey, TValue>` | values stored as references |

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

`Dict` hashes and compares keys by their raw bytes. That is exact for integers and for structs
without padding. It is not value equality for floats (0.0 and -0.0 differ, and NaN payloads do too)
or for structs whose padding bytes may differ, so avoid such keys or normalize them first.

## Sort and iterate

- `List.Sort(&Compare)` and `RefList.Sort` take the address of a static method that returns negative,
  zero or positive. It is a heapsort: no extra memory, and not stable.
- `foreach` works on `RefList`, `Dict`, `StrDict` and `RefDict`. Maps yield their pairs in no
  particular order. Don't `Set` or `Remove` during the loop: entries move, so some may be skipped or
  seen twice. For a `List`, loop over `Count` with `Get`, or over `AsSpan()`.

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
[`RefList`](../reference/reflist.html), [`StrDict`](../reference/strdict.html) and
[`RefDict`](../reference/refdict.html).

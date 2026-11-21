---
title: Memory without a GC
index: 2
---

# Memory without a GC

In zero mode, `new` calls `calloc` and nothing ever frees the result. There is no garbage collector to
notice that an object is no longer used. That is fine for objects that live as long as the program,
and it is a leak for everything else. This page explains how programs here handle the "everything
else": arenas, and the rules that keep them safe.

## `new` is for the long-lived

Classes, virtual methods and interfaces all work in zero mode. Each `new` just costs memory for the rest
of the run:

<!-- sample: samples/VirtualTest.cs new-leaks -->
```csharp
Out.Line("--- 3. Virtual methods on classes (each `new` leaks) ---");
Animal dog = new Dog();
Animal cat = new Cat();
Out.Line(dog.Sound());
Out.Line(cat.Sound());
```

Two objects allocated once are not a problem. The same two allocated per request, per frame or per
line of input are: the program grows until memory runs out. The same goes for boxing and for `a + b` on
strings, which also call `calloc`.

## Arenas free by lifetime

An arena, `lib/Arena.cs`, hands out memory by moving a pointer forward, and frees everything it handed
out at once. Freeing all at once is what makes it work without a GC: you don't track each object, you
track how long a group of objects lives.

So the rule is **one arena per lifetime**. A program typically has one arena that lives as long as the
program, one that is reset after every frame or request, and one for scratch work. You create each one
and pass it to whatever allocates; collections and strings live in the arena you give them. Arenas
are independent of each other:

<!-- sample: samples/Arena.cs second-arena -->
```csharp
// Arenas are independent: one per lifetime. Here a second arena holds a
// list for a while and is dropped without touching the first.
Arena frame = Arena.Create(256);
List<int> numbers = List<int>.Create(frame);
for (int i = 0; i < 100; i++)
    numbers.Add(i);
```

Destroying `frame` later frees the list without touching any other arena.

A few properties follow from the design:

- **An `Arena` is a handle**, one pointer. Passing or storing it is free, and copies refer to the same
  arena. `default(Arena)` is "not created": every allocation from it returns null, and so does
  everything built on it.
- **Allocation returns null on failure**, and leaves the arena as it was. `List.Add` and the like
  return false.
- **Blocks grow as needed.** Normal blocks grow in a Fibonacci sequence from the first block's size (for
  `Create(128)`: 128, 128, 256, 384, 640 and so on), and an allocation too
  big for the next block gets a block of its own. Every allocation picks its alignment.
- **Not thread-safe.** Use one arena per thread.

## Three ways to free

**`Destroy()`** returns all of an arena's memory to the system. **`Reset()`** frees every allocation but
keeps the memory: it merges the blocks into one, so a loop that resets each round stops calling
`malloc` after the first.

**`Save()` and `Restore(mark)`** free only what was allocated since the mark. They are for temporary
work in an arena that also holds data to keep:

<!-- sample: samples/Strings.cs restore -->
```csharp
// Temporary strings: free them with Restore and keep the rest.
nuint usedBefore = s_arena.Used;
Arena.Mark mark = s_arena.Save();
string temporary = Str.Format(s_arena, $"{name} has {name.Length} letters");
bool temporaryOk = temporary.EndsWith("3 letters");
unsafe { s_arena.Restore(mark); } // SAFETY: temporary is not used below.
```

Marks must be restored newest first. Restoring a mark from another arena, or from before a `Reset()`,
ends the program with a message, and so does restoring a mark that an older one already ended, unless
enough has been allocated since to cover its position. That last case can slip through, which is why
**`OpenScope()`** is the preferred form. It is `Save()` and `Restore()` as a `using` block: leaving the
block, by any path including an early return, frees what was allocated inside it, and the pairs can't
get out of order.

```csharp no-sample
Arena.Scope temp;
// SAFETY: nothing allocated inside the block is used after it.
unsafe { temp = arena.OpenScope(); }
using (temp)
{
    // temporary allocations
}
```

`Destroy`, `Reset`, `Restore` and `OpenScope` are marked `unsafe`, because calling them is what turns
other pointers into dangling ones. [The memory safety rules](safety-rules.html) explains what that
marking means.

## The Restore hazard

One mistake none of the above prevents. A `List`, `Dict` or `StrBuilder` created before the mark may
grow after it. Its new buffer is then allocated after the mark, so `Restore` frees it while the
collection still points at it. Later allocations overwrite it, and nothing reports that.

The rule: between `Save()` and `Restore()`, only add to collections created after the mark. When
temporary work has to add to long-lived collections, give it a separate **scratch arena** instead, and
copy the result out before freeing the scratch arena. With strings that copy is
`sb.ToArenaString(target)` or `Str.ToArenaString(target, s)`; with collections it is
`list.Clone(arena)` or `dict.Clone(arena)`. Copies are always explicit: `var b = a` on a `List`, `Dict`
or `StrBuilder` shares the contents, it doesn't copy them. [Keep a string](../how-to/keep-a-string.html)
shows the pattern.

## Running out of memory

By default, out of memory is a value you check: null from an allocation, false from `Add`. Where the
program couldn't carry on anyway, create the arena with `stopOnOutOfMemory: true`. It then ends the
program with a message instead, so nothing built on that arena has to check. That matters most on DOS
and WebAssembly, where memory really does run out.

## Seeing what an arena does

- **Names.** `Arena.Create(size, "frame")` names an arena, and failure messages then say which arena
  they are about. The arena keeps the reference, so pass a string that outlives it, such as a literal.
- **`Dump()`** prints how full the arena is and its blocks. Many small blocks mean the first block is
  too small for the workload.
- **`PeakUsed`** is the most the arena ever held at once. Run the workload, read it, and pass that as the
  first block's size, so the arena never has to grow.

## Catching stale pointers

By default, a stale pointer isn't detected. After `Reset()`, an old pointer still reads memory that
looks valid. `Arena.DebugChecks` is the debugging aid for that. Set it first thing in `Main`:

<!-- sample: samples/Arena.cs debug-checks -->
```csharp
// Debugging aid: arenas created while DebugChecks is on fill freed memory
// with 0xDD, so a stale pointer reads an obvious pattern instead of the
// old value, and collections stop the program when used after a reset.
Arena.DebugChecks = true;
Arena debug = Arena.Create(256);
int* stale = debug.AllocArray<int>(4);
unsafe { stale[0] = 12345; }
```

Arenas created afterwards fill freed memory with `0xDD` bytes, so a stale pointer reads `0xDDDDDDDD`
instead of plausible old data. `List`, `Dict` and `StrBuilder` also check on every use that their
memory is still allocated, and stop the program with a message if it isn't. That catches a collection
used after its arena was reset, and the Restore hazard above.

It can't catch a stale pointer into memory that has since been handed out again. And it costs a fill on
every free and a check on every collection operation, so leave it off in release builds.
[Catch arena mistakes](../how-to/catch-arena-mistakes.html) shows it in use, and the
[Arena reference](../reference/arena.html) lists every member.

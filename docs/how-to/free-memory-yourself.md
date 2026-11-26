---
title: Free memory yourself
index: 28
---

# Free memory yourself

Most zero-mode code takes its memory from an [arena](../concepts/memory.html) and frees it all at
once. Sometimes one block needs its own lifetime: a buffer whose size is known only when the program
runs and that outlives the arena scope it was made in, a block handed to C, or code shared with .NET
that already calls `NativeMemory`. For those, `NativeMemory.Alloc` and `Free` work as they do in .NET,
on the target's `calloc` and `free`. This guide allocates and frees a block, wraps one in a struct
that frees itself, and lists what differs from .NET and what nothing checks.

## Allocate and free a block

`Alloc(count, size)` returns a pointer to `count` items of `size` bytes each, and `Free` gives the
block back. Both need `using System.Runtime.InteropServices;`, and reading or writing through the
pointer needs an `unsafe` block ([The memory safety rules](../concepts/safety-rules.html)):

<!-- sample: samples/Core.cs native-memory -->
```csharp
// The attributes are for .NET's analyzers; zero mode ignores them.
[SupportedOSPlatform("linux"), SupportedOSPlatform("windows")]
[UnsupportedOSPlatform("browser")]
static bool AllocatesAndFrees(int count)
{
    unsafe
    {
        // SAFETY: items is freed once, after its last use.
        int* items =
            (int*)NativeMemory.Alloc((nuint)count, sizeof(int));
        for (int i = 0; i < count; i++)
            items[i] = i * 3;
        bool kept = true;
        for (int i = 0; i < count; i++)
            kept &= items[i] == i * 3;
        NativeMemory.Free(items);
        return kept;
    }
}
```

Every block must be freed exactly once, after its last use. Nothing frees it for you: a block you
forget stays allocated until the program ends.

## Wrap a block in a struct

A raw pointer doesn't know how long it is or when to go away. A small struct can hold both, hand out
a span, and free the block in `Dispose`:

<!-- sample: samples/Core.cs native-buffer -->
```csharp
// Owns one block from NativeMemory. Dispose frees it, so don't copy it:
// two copies would free the same block twice.
public struct NativeBuffer<T> : IDisposable where T : unmanaged
{
    T* _items;
    int _length;

    public NativeBuffer(int length)
    {
        _items = Allocate(length);
        _length = length;
    }

    public int Length => _length;

    /// <safety>The span is invalidated by Resize and Dispose.</safety>
    public unsafe Span<T> AsSpan()
    {
        unsafe { return new Span<T>(_items, _length); }
    }

    // Realloc keeps the items and may move them; what it adds
    // isn't zeroed, so Resize clears it.
    public void Resize(int length)
    {
        unsafe
        {
            // SAFETY: _items came from NativeMemory, and the new block
            // holds `length` items.
            _items = (T*)NativeMemory.Realloc(
                _items, (nuint)length * (nuint)sizeof(T));
            if (length > _length)
                new Span<T>(_items + _length, length - _length).Clear();
        }
        _length = length;
    }

    public void Dispose()
    {
        // SAFETY: _items is set to null, so a second Dispose frees nothing.
        unsafe { NativeMemory.Free(_items); }
        _items = null;
        _length = 0;
    }

    static T* Allocate(int length)
    {
        unsafe
        {
            return (T*)NativeMemory.AllocZeroed(
                (nuint)length, (nuint)sizeof(T));
        }
    }
}
```

With `using`, the block is freed when the method returns, by any path:

<!-- sample: samples/Core.cs native-buffer-use -->
```csharp
static bool GrowsAndFrees(int count)
{
    // Dispose runs when the method returns, by any path.
    using var squares = new NativeBuffer<int>(count);
    unsafe
    {
        // SAFETY: each span is used before the next Resize.
        Span<int> items = squares.AsSpan();
        for (int i = 0; i < items.Length; i++)
            items[i] = i * i;

        squares.Resize(count * 2);
        items = squares.AsSpan();
        return items[count - 1] == (count - 1) * (count - 1)
            && items[count] == 0;
    }
}
```

Two things to keep in mind:

- **`AsSpan` is `unsafe`**, like `List<T>.AsSpan` in `lib/`. `Realloc` may move the block and free
  the old one, so a span taken before `Resize` may point at freed memory. Take a new span after
  every `Resize`.
- **Don't copy the struct.** Assigning it, or passing it by value, gives two structs with the same
  pointer. Disposing one leaves the other dangling, and disposing both frees the block twice. Pass it
  with `ref` instead ([Pass structs by reference](pass-structs-by-ref.html)).

## What differs from .NET

- **Memory comes back zeroed.** `calloc` zeroes what it returns, so `Alloc` gives the same zeroed
  memory as `AllocZeroed`. .NET's `Alloc` doesn't, so code shared with .NET shouldn't rely on it.
- **Running out ends the program.** Where .NET throws `OutOfMemoryException`, for a size the system
  can't give or a count times size that overflows, zero mode stops with a message:

  ```text
  NativeMemory: the size overflows
  ```

  There's no exception to catch, so check a size that comes from input before you allocate it.
- **`Alloc(0)` returns a valid pointer** that `Free` takes, and `Free(null)` does nothing, as in .NET.
- **`Realloc` doesn't zero what it adds**, as in .NET, though `Alloc` does here. Clear it as
  `Resize` does above. If `Realloc` runs out, the program ends.
- **Some targets have little to give.** On the C64 one block can't be larger than 65,520 bytes, and
  the Arduino Uno R4 has 32 KB of RAM for everything ([Build for
  microcontrollers](build-for-microcontrollers.html)).

## What nothing checks

`Arena.DebugChecks` ([Catch arena mistakes](catch-arena-mistakes.html)) has no counterpart here. These
are the same mistakes they are in C, and nothing reports them:

- using a block after `Free`,
- freeing a block twice, or freeing a pointer that didn't come from `Alloc`,
- reading or writing past the end of a block,
- forgetting to free it.

That's the reason to prefer an arena wherever blocks can share a lifetime: freeing them together
removes most of these mistakes, and `DebugChecks` catches many of the rest.

## Share the code with .NET

The same code compiles for .NET, where `NativeMemory` is the real one. The `[SupportedOSPlatform]`,
`[UnsupportedOSPlatform]` and `[ConstantExpected]` attributes compile in zero mode too, so code that
carries them for .NET's analyzers needs no `#if`. Zero mode ignores them.

See also: [Memory and arenas](../concepts/memory.html), [Limits](../concepts/limits.html#nativememory),
and [Build a native library](build-a-native-library.html). A library that hands a block to its
caller should also export a function that frees it, so the block goes back to the allocator it came
from.

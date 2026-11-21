---
title: Keep a string
index: 2
---

# Keep a string

Most text in a zero-mode program lives somewhere short-lived: a line read into a stack buffer, a
string formatted in a scratch arena that is reset every round, the contents of a `StrBuilder`. To keep
one past that, copy it into an arena that lives as long as you need the text. This guide shows the
three ways to make that copy and how to collect the results.

## Decide where the string will live

Create an arena for the lifetime of what you keep. The quiz keeps its mistakes until the end of the
run, so it creates one arena for them, and the list that holds them lives there too:

<!-- sample: samples/Quiz.cs keep -->
```csharp
// Lives until the end: the mistakes are kept here, the list and its strings.
Arena keep = Arena.Create(4096);
RefList<string> mistakes = RefList<string>.Create(keep);
if (!mistakes.IsCreated)
{
    Out.Line("Arena.Create failed");
    return 1;
}
```

`RefList<string>.Create` returns a not-created list if the arena couldn't be created or is full, which
`IsCreated` tells you.

## Copy a span: `Str.ToArenaString`

A line from `In.ReadLine` is a slice of your buffer and is overwritten by the next read.
`Str.ToArenaString(arena, span)` copies it into a new string in the arena:

```csharp no-sample
string kept = Str.ToArenaString(keep, buffer.Slice(0, length));
```

Any `ReadOnlySpan<char>` works, and a `string` converts to one implicitly, so the same call copies a
string out of an arena that is about to be reset.

## Format straight into the long-lived arena: `Str.Format`

When the string you keep is formatted anyway, format it in the arena it will live in. No copy is
needed:

<!-- sample: samples/Quiz.cs record -->
```csharp
else
{
    Out.Line($"no, it's {a + b}");
    // Kept until the end, so the string is built in `keep`, not in
    // the buffer the next answer overwrites.
    mistakes.Add(Str.Format(keep, $"  {a} + {b} = {a + b}, not {value}"));
}
```

`Str.Format(arena, $"...")` builds the string in that arena and returns it. The holes are formatted
straight into arena memory (see [Format text](format-text.html)).

## Build in scratch, keep only the result: `StrBuilder.ToArenaString`

A `StrBuilder` that grows can leave its old buffers behind in its arena until that arena is reset. Build
it in a scratch arena, copy the finished text into the arena that keeps it, and then reset or destroy
the scratch arena:

```csharp no-sample
string kept = draft.ToArenaString(keep);
```

`ToArenaString()` without an argument copies into the builder's own arena. Both return null if the
arena is out of memory.

## Collect the strings

`List<T>` takes only unmanaged types, so strings go in a `RefList<string>`, and `foreach` gives them
back:

<!-- sample: samples/Quiz.cs report -->
```csharp
Out.Line($"{right} of {asked} right");
if (mistakes.Count > 0)
{
    Out.Line("Mistakes:");
    foreach (string mistake in mistakes)
        Out.Line(mistake);
}
```

A `RefList` stores references, not copies. The list can live in one arena and the strings in another,
but every string must outlive the list's use of it. A string built in a scratch arena dangles once
that arena is reset, even if the list lives elsewhere. `RefDict<TKey, string>` follows the same rule
for its values.

### Keys are copied, values aren't

`StrDict<TValue>` looks up keys by their text and takes them as `ReadOnlySpan<char>`. A new key is
copied into the map's own arena, so a key read into a buffer that is then overwritten is safe. In
`samples/Collections.cs`, `counts` is a `StrDict<int>` and every word is copied into the same
8-character `wordBuffer` before it is counted:

<!-- sample: samples/Collections.cs word-counts -->
```csharp
foreach (ReadOnlySpan<char> w in "the cat and the hat and the bat".Split(' '))
{
    for (int i = 0; i < w.Length; i++)
        wordBuffer[i] = w[i];
    ReadOnlySpan<char> key = wordBuffer.Slice(0, w.Length);
    counts.TryGet(key, out int n);
    counts.Set(key, n + 1);
}
```

The same holds for keys formatted in a scratch arena: the sample sets 500 keys made with
`Str.Format(scratch, $"key{i}")`, resetting `scratch` before each one, and later removes and finds
them by text with keys made the same way.

Values are stored as they are. A `StrDict<string>` holds references to its value strings, exactly like
`RefList`, so a value string must live as long as the map uses it:

<!-- sample: samples/Collections.cs labels -->
```csharp
var labels = StrDict<string>.Create(arena); // values can be references too
labels.Set("ok", "fine");
labels.Set("ok", Str.Format(arena, $"fine x{2}"));
```

Here the formatted value lives in the same arena as the map, so they go together. The literal `"fine"`
isn't in any arena, and the sample stores it directly.

`Clone(target)` on a `StrDict` copies the keys into the target arena too; values, again, are copied as
they are.

## Watch out for

- **Check for null when memory can run out.** `Str.ToArenaString`, `Str.Format` and `Str.Concat` return
  null if the arena isn't created or is full. An arena created with `stopOnOutOfMemory: true` ends the
  program instead, so nothing needs checking (see
  [Catch arena mistakes](catch-arena-mistakes.html)).
- **`a + b` never frees.** `+` on strings allocates with calloc, and nothing is ever freed. That is
  fine once; in a loop, use `Str.Concat(arena, a, b)`.
- **Freeing the arena frees the strings.** `Restore`, `Reset` and `Destroy` free the strings in an
  arena along with everything else. Nothing reports a stale string afterwards: under
  `Arena.DebugChecks` its memory reads `0xDD` bytes, but only collections check themselves.
- **A removed key's copy stays.** `StrDict.Remove` leaves the copied key in the arena until the arena
  is reset.

See also: [Strings without a garbage collector](../concepts/strings.html),
[Memory and arenas](../concepts/memory.html), the [`Str`](../reference/str.html),
[`StrBuilder`](../reference/strbuilder.html), [`RefList`](../reference/reflist.html) and
[`StrDict`](../reference/strdict.html) reference, and the tutorial's
[Keep the mistakes](../tutorial/keep-the-mistakes.html).

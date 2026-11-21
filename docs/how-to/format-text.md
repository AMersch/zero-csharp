---
title: Format text
index: 4
---

# Format text

zerocore's `string` can't take `$"..."` with holes that aren't strings, and `ToString` and `+`
allocate a new string each time, never freed. `lib/` takes `$"..."` through its own interpolated
string handlers instead. Each
hole is formatted as the compiler visits it, straight into wherever the text is going: stdout, a new
string in an arena, or a `StrBuilder`. This guide shows the three destinations and the formats a
hole accepts.

## Write to stdout: `Out.Write` and `Out.Line`

`Out.Line($"...")` writes the text and a newline into `Out`'s buffer, and no string is built:

<!-- sample: samples/QuizStep2.cs ask -->
```csharp
// In.ReadLine flushes Out first, so the prompt shows before the
// program waits for the answer.
Out.Write($"Q{i}: {a} + {b} = ");
int length = In.ReadLine(buffer);
if (length < 0)
{
    Out.Line();
    Out.Line("(end of input)");
    break;
}
asked++;
```

`Out.Write` leaves the line open, as for a prompt. `Out.Line` flushes the buffer after the newline;
output without a newline waits until the buffer is full, `Out.Flush()` is called, or `In.ReadLine`
flushes it. Call `Out.Flush()` before the program ends if the last thing written had no newline.

## Make a string: `Str.Format(arena, $"...")`

When you need a real `string` (to store, to pass on, to use as a key), format it into an arena:

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

The result is an ordinary string laid out in arena memory, so it works anywhere a string does, until
the arena is reset or destroyed. `Str.Format` returns null if the arena isn't created or is full. To
keep the string past a scratch arena, see [Keep a string](keep-a-string.html).

## Append to a builder: `StrBuilder.Append($"...")`

A `StrBuilder` collects text in its arena over several calls. `Append` takes `$"..."` too, and returns
false if the builder isn't created or the arena is full:

```csharp no-sample
sb.Append($"{count} items, {price:F2} each");
```

Read the result with `sb.AsSpan()` (valid until the next `Append`), or copy it into a string with
`sb.ToArenaString(target)`.

## Holes: values, alignment and format

A hole takes integers, `double` and `float`, `bool`, `char`, strings and spans. After the value comes
an optional alignment, `{x,8}` or `{x,-8}`, and an optional format, `{x:F2}`, or both, `{x,6:F1}`.
These results are checked by `samples/Strings.cs`:

| Hole | Result | Why |
|---|---|---|
| `{42,5}` | `   42` | a positive width pads on the left |
| `{42,-5}` | `42   ` | a negative width pads on the right |
| `{"toolong",3}` | `toolong` | a value wider than the width isn't cut |
| `{7:D3}` | `007` | `Dn`: at least n digits |
| `{-7:D3}` | `-007` | the sign doesn't count as a digit |
| `{255:X}` | `FF` | `X`: hex, upper case; `x` for lower case, `Xn` for at least n digits |
| `{3.14159:F2}` | `3.14` | `Fn`: n decimals (`F` alone means 2) |
| `{2.5:F0}` | `3` | rounding is half away from zero |
| `{1.5f:F3}` | `1.500` | a `float` is formatted as a `double` |
| `{3.5,6:F1}` | `   3.5` | alignment and format together |
| `{1e20:F2}` | `1.00e+20` | values of 1.8e19 and up switch to exponent form |
| `{true}` | `true` | not `True` as in .NET |

Without a format, a `double` prints up to 6 decimals with trailing zeros removed. The format letters
`d` and `f` work as `D` and `F`. An unknown format falls back to the default instead of failing.

To line up a table, give every column a width. `samples/QuizStep1.cs` pads each number with `{a,2}` so
the questions line up, and `samples/Strings.cs` prints its table with
`{"apple",-8}|{3,5}|{1.25,9:F2}`.

## What's not supported

- **`{-1:X}` isn't the type's width.** Every integer is formatted as a 64-bit `long` or `ulong`, so
  `{-1:X}` prints 16 hex digits whatever the type of the value.
- **No holes of other types.** A struct or class in a hole doesn't compile. Format its fields
  instead, or call its `ToString()`, which allocates.
- **No culture, and few formats.** Formatting is invariant: `.` as the decimal point, no thousands
  separators. Integers know `D` and `X`, doubles know `F`; any other format, such as `N`, `E` or `P`,
  is ignored and the value prints in the default form.
- **`string.Format` and numbers in `+`** (`"n=" + 5`) work, but allocate a string each time with
  calloc, never freed, and `string.Format` knows only `{0}`-style holes. In a loop, use
  `Str.Format(arena, $"n={5}")`.
- **Plain `$"..."` as a string** works only when every hole is a string, through `+`, and then it
  allocates with calloc and is never freed. Prefer `Str.Format`.

## Watch out for

- **Temporary strings belong in a scratch arena.** Every `Str.Format` takes memory from its arena until
  that arena is reset. A string you only print needs no arena at all: write it with `Out.Line`.
- **Output is UTF-8.** `Out` encodes what you write as UTF-8, and an unpaired surrogate becomes U+FFFD.

See also: [Strings without a garbage collector](../concepts/strings.html), and the reference pages for
[`Out`](../reference/out.html), [`Str`](../reference/str.html) and
[`StrBuilder`](../reference/strbuilder.html).

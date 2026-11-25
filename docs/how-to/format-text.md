---
title: Format text
index: 4
---

# Format text

`ToString`, `string.Format` and a plain `$"..."` work as in .NET, with .NET's format strings, but
each allocates a new string that is never freed. `lib/` takes `$"..."` through its own interpolated
string handlers instead. Each
hole is formatted as the compiler visits it, straight into wherever the text is going: stdout, a new
string in an arena, or a `StrBuilder`. This guide shows the three destinations, the formats a
hole accepts, and [.NET's format strings](#nets-format-strings-tostring-string.format-and-tryformat)
for when a string is what you need.

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

## .NET's format strings: `ToString`, `string.Format` and `TryFormat`

Every number (the integers, `double`, `float`, `Half` and `decimal`) takes .NET's standard format
strings, with .NET's output character for character, in the invariant culture. In these excerpts
from `samples/Formats.cs`, `Is(text, expected)` checks that the two strings are equal:

<!-- sample: samples/Formats.cs integers -->
```csharp
int count = 1234567;
Is(count.ToString("N0"), "1,234,567");
Is(count.ToString("D9"), "001234567");
Is(255.ToString("X4"), "00FF");
Is((-1).ToString("x"), "ffffffff");
Is(5.ToString("B8"), "00000101");
```

| Format | Means | Default precision |
|---|---|---|
| `D`n | integers: at least n digits | |
| `X`n, `x`n | integers: hex, at least n digits; a negative value prints its two's complement at the type's width | |
| `B`n | integers: binary | |
| `F`n | n decimals | 2 |
| `N`n | n decimals, with thousands separators | 2 |
| `E`n | scientific, n decimals: `1.23E+003` | 6 |
| `G`n | n significant digits, scientific when that's shorter | all digits |
| `P`n | multiplied by 100, with ` %` | 2 |
| `C`n | the invariant currency sign: `¤1,234.50`, `(¤1,234.50)` | 2 |
| `R` | the shortest text that reads back as the same value | |

`double`, `float` and `Half` print the exact binary value, rounded to what the format asks for, ties to
the even digit. Without a format they print the shortest text that reads back, as .NET Core 3.0 and
later do:

<!-- sample: samples/Formats.cs doubles -->
```csharp
double sum = 0.1 + 0.2;
Is(sum.ToString(), "0.30000000000000004");
Is(sum.ToString("F2"), "0.30");
Is(1234.5678.ToString("N2"), "1,234.57");
Is(0.1234.ToString("P1"), "12.3 %");
Is(1234.56.ToString("E2"), "1.23E+003");
// 0.125 is exact in binary: a tie, so the even digit.
Is(0.125.ToString("F2"), "0.12");
```

`decimal` and the integers round ties away from zero, and `decimal` keeps its trailing zeros:

<!-- sample: samples/Formats.cs decimal -->
```csharp
decimal price = 2.675m;
Is(price.ToString("F2"), "2.68");
Is(1.50m.ToString(), "1.50");
Is(1234567.891m.ToString("N"), "1,234,567.89");
Is((-0.001m).ToString("F2"), "0.00");
```

`string.Format` and `StringBuilder.AppendFormat` take .NET's holes, `{index[,alignment][:format]}`, and
a plain `$"..."` kept as a string compiles to `string.Format`:

<!-- sample: samples/Formats.cs format -->
```csharp
string row = string.Format("[{0,5}][{0,-5}][{1,8:F2}]", 42, 3.14159);
Is(row, "[   42][42   ][    3.14]");
double celsius = 21.456;
Is($"{celsius:F1} °C", "21.5 °C");
Is($"|{255,-6:x}|{5,6:B4}|", "|ff    |  0101|");
```

`TryFormat` writes into a span you provide and allocates nothing. It returns false, having written
nothing, when the span is too short:

<!-- sample: samples/Formats.cs tryformat -->
```csharp
Span<char> buffer = stackalloc char[16];
bool fits = 1234.5.TryFormat(buffer, out int written, "N1");
Check("TryFormat writes into the span",
    fits && buffer.Slice(0, written).SequenceEqual("1,234.5"));
Check("TryFormat that doesn't fit writes nothing",
    !long.MinValue.TryFormat(buffer.Slice(0, 8), out written, "N0")
    && written == 0);
```

Every overload takes an `IFormatProvider` and ignores it: there are no cultures. A custom format such
as `"0.00"` or `"#,##0"` isn't supported and ends the program with a `FormatException`, as does a
malformed hole such as `"{0:F2"`. `samples/Formats.cs` checks these results on every target, and
`scripts/format-check/check-formats.sh` compares the formatting with .NET's over millions of values.

The holes of `Out`, `Str.Format` and `StrBuilder` above don't use these formats yet: they have their
own, smaller set, and differ from .NET in places (`{2.5:F0}` is `3`, where .NET prints `2`).

## What's not supported

- **`{-1:X}` isn't the type's width.** Every integer is formatted as a 64-bit `long` or `ulong`, so
  `{-1:X}` prints 16 hex digits whatever the type of the value.
- **No holes of other types.** A struct or class in a hole doesn't compile. Format its fields
  instead, or call its `ToString()`, which allocates.
- **No culture, and few formats in holes.** Formatting is invariant: `.` as the decimal point, no
  thousands separators. In `Out`, `Str.Format` and `StrBuilder` holes, integers know `D` and `X` and
  doubles know `F`; any other format, such as `N`, `E` or `P`, is ignored and the value prints in the
  default form. `ToString` and `string.Format` know them all (see above).
- **`ToString`, `string.Format`, plain `$"..."` and numbers in `+`** (`"n=" + 5`) work, but
  allocate a string each time with calloc, never freed. In a loop, use `Str.Format(arena, $"n={5}")`,
  or `TryFormat` into a span.

## Watch out for

- **Temporary strings belong in a scratch arena.** Every `Str.Format` takes memory from its arena until
  that arena is reset. A string you only print needs no arena at all: write it with `Out.Line`.
- **Output is UTF-8.** `Out` encodes what you write as UTF-8, and an unpaired surrogate becomes U+FFFD.

See also: [Strings without a garbage collector](../concepts/strings.html), and the reference pages for
[`Out`](../reference/out.html), [`Str`](../reference/str.html) and
[`StrBuilder`](../reference/strbuilder.html).

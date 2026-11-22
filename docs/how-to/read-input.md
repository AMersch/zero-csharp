---
title: Read input from the user
index: 1
---

# Read input from the user

zerocore has no `Console`, so there is no `Console.ReadLine()` returning a new string. `lib/In.cs` reads
a line into a `Span<char>` you pass instead. Nothing is allocated per line, which matters when nothing
is ever freed. This guide reads answers the way `samples/Quiz.cs` does.

## Give the line somewhere to go

A buffer on the stack is enough for short answers. `stackalloc` is safe code under the updated memory
safety rules; only dereferencing a pointer needs `unsafe`.

<!-- sample: samples/QuizStep2.cs buffer -->
```csharp
// Each answer is read into this; nothing is allocated per question.
Span<char> buffer = stackalloc char[32];
int right = 0, asked = 0;
```

The buffer's size is the longest line you accept, in UTF-16 characters. Pick it for your input: the
quiz takes numbers, so 32 is plenty.

## Prompt, then read

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

`Out.Write` puts the prompt in `Out`'s buffer without a newline. `Out` writes its buffer out only when
`Out.Line` ends a line, when the buffer is full, or on `Out.Flush()`. `In.ReadLine` calls
`Out.Flush()` before it reads, so the prompt is on the screen before the program waits. You don't have
to flush yourself.

`In.ReadLine(buffer)` returns one of three things:

| Return value | Meaning | The buffer holds |
|---|---|---|
| `-1` | the end of input: Ctrl-D in a terminal, or the end of a file | nothing new |
| `0` to `buffer.Length` | a line of that length | the whole line |
| more than `buffer.Length` | a line too long for the buffer | its first `buffer.Length` characters |

A last line without a newline at the end is still returned as a line; the next call returns `-1`.

## Handle every case

The quiz's final loop asks again when the answer doesn't fit or isn't a number, and lets an empty
answer skip the question:

<!-- sample: samples/QuizStep3.cs loop -->
```csharp
while (true)
{
    Out.Write($"Q{i}: {a} + {b} = ");
    int length = In.ReadLine(buffer);
    if (length < 0)
    {
        Out.Line();
        Out.Line("(end of input)");
        asked--;
        goto Done;
    }
    // A line longer than the buffer returns its full length; the
    // buffer holds only its start, so ask again.
    if (length > buffer.Length)
    {
        Out.Write($"that's {length} characters; ");
        Out.Line($"answers are at most {buffer.Length}");
        continue;
    }

    ReadOnlySpan<char> answer = buffer.Slice(0, length).Trim();
    if (answer.Length == 0)
    {
        Out.Line("skipped");
        break;
    }
    if (!Str.TryParse(answer, out long value))
    {
        Out.Line($"\"{answer}\" isn't a number");
        continue;
    }

    if (value == a + b)
    {
        right++;
        Out.Line("right");
    }
    else
    {
        Out.Line($"no, it's {a + b}");
    }
    break;
}
```

- **Too long:** `In` has already read and dropped the rest of the line, so the next call starts on a
  fresh line. The return value is the full length, so you can say how long the line was.
- **The line itself** is `buffer.Slice(0, length)`. Only slice after checking both `length < 0` and
  `length > buffer.Length`; the tutorial's step 2 takes `Math.Min(length, buffer.Length)` instead, which
  accepts the start of a long line.
- **Whitespace:** `Trim()` on a span returns a span; nothing is copied.

## Parse numbers

`Str.TryParse` has overloads for `int`, `long`, `ulong` and `double`, and `Str.TryParseHex` reads hex
digits with an optional `0x`. They take a `ReadOnlySpan<char>`, so the slice goes straight in. Parsing
is strict and invariant: no surrounding whitespace (call `Trim()` first), no thousands separators. On
malformed input or overflow they return `false` and set the value to 0.

## What reaches the program

- **UTF-8.** Input is decoded as UTF-8. An invalid byte becomes U+FFFD (`�`). A character outside the
  Basic Multilingual Plane, such as an emoji, takes two `char`s in the buffer, so it counts as 2 towards
  the length.
- **Line endings.** Both `\n` and `\r\n` end a line, and neither is stored. A file written on Windows
  reads the same as one written on Linux.
- **Input from a file.** Every target passes standard input through. A program built with the SDK
  reads it like any other: `bin/Debug/zero03/Quiz < answers.txt`. In the repository:
  `./run.sh Quiz < answers.txt`, `./run.sh --wasm Quiz < answers.txt`, and on DOS
  `./run.sh --dos Quiz < answers.txt` (see [Build for DOS](build-for-dos.html)).

`test.sh` feeds Quiz `tests/input/Quiz.txt`, which holds a word, bad UTF-8, an over-long line, a CRLF
line, an empty line and a last line without a newline:

```bash
./run.sh Quiz < tests/input/Quiz.txt
```

```text
6 questions. An empty answer skips one.
Q1: 38 + 8 = "abc" isn't a number
Q1: 38 + 8 = right
Q2: 5 + 0 = "é😀" isn't a number
Q2: 5 + 0 = "��(" isn't a number
Q2: 5 + 0 = no, it's 5
Q3: 12 + 5 = that's 40 characters; answers are at most 32
Q3: 12 + 5 = right
Q4: 2 + 43 = skipped
Q5: 27 + 8 = right
Q6: 38 + 36 = 
(end of input)
3 of 5 right
Mistakes:
  5 + 0 = 5, not 7
```

The answers from the file aren't echoed, which is why each prompt runs into the reply.

## Watch out for

- **The line lives only until the next read.** The next `In.ReadLine` into the same buffer overwrites
  it. To keep a line, copy it into an arena: `Str.ToArenaString(arena, line)`. See
  [Keep a string](keep-a-string.html).
- **Read standard input only through `In`.** `In` reads it 256 bytes at a time into a buffer of its
  own, so a direct `read()` call elsewhere would miss the bytes `In` has already taken.

See also: [`In` reference](../reference/in.html), [`Str` reference](../reference/str.html),
[Strings without a garbage collector](../concepts/strings.html), and the tutorial pages
[Read the answers](../tutorial/read-answers.html) and
[Handle bad input](../tutorial/handle-bad-input.html).

---
title: Handle bad input
index: 3
---

# Step 3: handle bad input

Step 2 treats anything that isn't the right number as a wrong answer, including a typo. This step asks
again instead, and lets an empty answer skip the question. The question's body becomes a loop that
ends when the answer has been dealt with:

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

Three cases are new:

- **Too long.** `In.ReadLine` returns the line's full length even when the buffer can't hold it, and
  skips the rest of the line, so the next read starts on the next line. `length > buffer.Length` is how
  the program knows. Returning the length instead of failing lets the caller decide: here, ask again.
- **Empty.** After `Trim()`, an empty span means the answer was blank: skip the question.
- **Not a number.** `Str.TryParse` returns false. The message quotes the answer with `{answer}`: a span
  can be a hole in `$"..."`, so the text goes from the input buffer to the output buffer without a
  string in between.

`goto Done` leaves both loops at the end of input. C#'s `goto` is as unfashionable as ever, but it is
the plainest way out of two loops.

## Run it

The test suite feeds this step a file with every kind of bad input: letters, characters outside ASCII,
bytes that aren't valid UTF-8, a 40-character line, a Windows line ending, an empty line, and a last
line without a line ending.

```bash
./build.sh QuizStep3
./run.sh QuizStep3 < tests/input/QuizStep3.txt
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
```

The invalid bytes came out as `�`, the Unicode replacement character: `In` decodes UTF-8 and replaces
what it can't decode, so a program never sees a broken character. `"é😀"` went in as UTF-8, became
UTF-16 `char`s in the span, and came back out as UTF-8 through `Out`.

Next: [keep the mistakes](keep-the-mistakes.html).

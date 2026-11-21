// Example: reading input with In (lib/In.cs). Asks arithmetic questions in a
// loop and reads the answers; asks again when an answer isn't a number.
// Also the tutorial's last step (QuizStep1-3.cs are the earlier ones): it keeps
// the mistakes in an arena to list them at the end.
// Build: ./build.sh Quiz
// Run:   ./run.sh Quiz                (type the answers; Ctrl-D ends early)
//        ./run.sh Quiz < answers.txt

using System;

namespace QuizSample;

public static class Program
{
    const int Questions = 6;

    static uint s_seed = 2026;

    // The same questions every run, so a file of answers fits them.
    static int Next(int limit)
    {
        s_seed = s_seed * 1103515245 + 12345;
        return (int)((s_seed >> 16) % (uint)limit);
    }

    public static int Main()
    {
        // docs:keep
        // Lives until the end: the mistakes are kept here, the list and its strings.
        Arena keep = Arena.Create(4096);
        RefList<string> mistakes = RefList<string>.Create(keep);
        if (!mistakes.IsCreated)
        {
            Out.Line("Arena.Create failed");
            return 1;
        }
        // docs:end

        // Each answer is read into this; nothing is allocated per question.
        Span<char> buffer = stackalloc char[32];
        int right = 0, asked = 0;

        Out.Line($"{Questions} questions. An empty answer skips one.");
        for (int i = 1; i <= Questions; i++)
        {
            int a = Next(50), b = Next(50);
            asked++;
            while (true)
            {
                // The prompt is formatted straight into Out's buffer; In.ReadLine
                // flushes it, so it shows before the program waits.
                Out.Write($"Q{i}: {a} + {b} = ");
                int length = In.ReadLine(buffer);
                if (length < 0)
                {
                    Out.Line();
                    Out.Line("(end of input)");
                    asked--;
                    goto Done;
                }
                if (length > buffer.Length)
                {
                    Out.Line($"that's {length} characters; answers are at most {buffer.Length}");
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
                // docs:record
                else
                {
                    Out.Line($"no, it's {a + b}");
                    // Kept until the end, so the string is built in `keep`, not in
                    // the buffer the next answer overwrites.
                    mistakes.Add(Str.Format(keep, $"  {a} + {b} = {a + b}, not {value}"));
                }
                // docs:end
                break;
            }
        }

    Done:
        // docs:report
        Out.Line($"{right} of {asked} right");
        if (mistakes.Count > 0)
        {
            Out.Line("Mistakes:");
            foreach (string mistake in mistakes)
                Out.Line(mistake);
        }
        // docs:end
        Out.Flush();
        return 0;
    }
}

// Example: a small language model telling a story (lib/Llama.cs), with the
// model compiled into the program.
// Build: ./build.sh Llama    Run: ./run.sh Llama [-n steps] [-t temperature] [-p top-p] [-s seed] [-i prompt] [-v]
//
// The model is Karpathy's stories260K (models/): about 260,000 parameters
// trained on children's stories, and its 512-token tokenizer. build.sh turns
// both into byte literals, so the program reads them in place, with no files.
// By default it continues "Once upon a time" greedily for 64 tokens, which is
// the same story every run and on every target; -t picks words by chance.

using System;

namespace LlamaSample;

public static class Program
{
    static void Usage()
    {
        Out.Line("Usage: Llama [-n steps] [-t temperature] [-p top-p] [-s seed] [-i prompt] [-v]");
        Out.Line("  -n  tokens to produce, the prompt's included (default 64)");
        Out.Line("  -t  0 picks the likeliest token (default); higher is more random");
        Out.Line("  -p  with -t, choose only among the likeliest tokens that add up to p (default 0.9)");
        Out.Line("  -s  random seed, for -t (default 42)");
        Out.Line("  -i  the start of the story (default \"Once upon a time\")");
        Out.Line("  -v  print the token ids and the speed");
    }

    public static int Main(string[] args)
    {
        int steps = 64;
        double temperature = 0, topP = 0.9;
        long seed = 42;
        string prompt = "Once upon a time";
        bool verbose = false;
        for (int i = 0; i < args.Length; i++)
        {
            string flag = args[i];
            if (flag == "-v")
            {
                verbose = true;
                continue;
            }
            if (i + 1 >= args.Length)
            {
                Usage();
                return 1;
            }
            string value = args[++i];
            bool ok = flag switch
            {
                "-n" => Str.TryParse(value, out steps),
                "-t" => Str.TryParse(value, out temperature),
                "-p" => Str.TryParse(value, out topP),
                "-s" => Str.TryParse(value, out seed),
                "-i" => (prompt = value) != null,
                _ => false,
            };
            if (!ok)
            {
                Usage();
                return 1;
            }
        }

        // docs:create
        Arena arena = Arena.Create(64 * 1024, "llama", stopOnOutOfMemory: true);
        Llama llama = Llama.Create(LlamaModel.Model, LlamaModel.Tokenizer, arena);
        // docs:end
        if (!llama.IsCreated)
        {
            Out.Line("The model or the tokenizer is malformed.");
            return 1;
        }
        if (steps < 1 || steps > llama.SeqLen)
            steps = llama.SeqLen;

        // The prompt as UTF-8, then as tokens, in the arena.
        Span<byte> text;
        Span<int> promptIds;
        unsafe
        {
            // SAFETY: both stay inside the arena, which lives until Main returns.
            text = new Span<byte>(arena.AllocArray<byte>(prompt.Length * 3), prompt.Length * 3);
            promptIds = new Span<int>(arena.AllocArray<int>(prompt.Length * 3 + 2), prompt.Length * 3 + 2);
        }
        int textLength = Utf8(prompt, text);
        int promptCount = llama.Encode(text.Slice(0, textLength), promptIds);

        LlamaSampler sampler = LlamaSampler.Create(llama.VocabSize, (float)temperature, (float)topP, (ulong)seed, arena);

        // docs:generate
        int token = promptIds[0]; // BOS
        for (int position = 0; position + 1 < steps; position++)
        {
            unsafe
            {
                // SAFETY: Forward's logits are read before the next call.
                float* logits = llama.Forward(token, position);
                int next = position + 1 < promptCount
                    ? promptIds[position + 1]           // still reading the prompt
                    : sampler.Sample(logits, llama.VocabSize);
                if (next == 1)
                    break; // BOS: the story is over
                Out.WriteUtf8(llama.Decode(token, next));
                if (verbose)
                    Out.Write($"[{next}]");
                token = next;
            }
        }
        Out.Line();
        // docs:end
        return 0;
    }

    // UTF-16 to UTF-8; returns the byte count. output needs 3 bytes per char.
    static int Utf8(string s, Span<byte> output)
    {
        int n = 0;
        for (int i = 0; i < s.Length; i++)
        {
            int c = s[i];
            if (c >= 0xD800 && c <= 0xDBFF && i + 1 < s.Length && s[i + 1] >= 0xDC00 && s[i + 1] <= 0xDFFF)
                c = 0x10000 + ((c - 0xD800) << 10) + (s[++i] - 0xDC00);
            if (c < 0x80)
            {
                output[n++] = (byte)c;
            }
            else if (c < 0x800)
            {
                output[n++] = (byte)(0xC0 | (c >> 6));
                output[n++] = (byte)(0x80 | (c & 0x3F));
            }
            else if (c < 0x10000)
            {
                output[n++] = (byte)(0xE0 | (c >> 12));
                output[n++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                output[n++] = (byte)(0x80 | (c & 0x3F));
            }
            else
            {
                output[n++] = (byte)(0xF0 | (c >> 18));
                output[n++] = (byte)(0x80 | ((c >> 12) & 0x3F));
                output[n++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                output[n++] = (byte)(0x80 | (c & 0x3F));
            }
        }
        return n;
    }
}

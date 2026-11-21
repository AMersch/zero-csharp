---
title: Run a language model
index: 13
---

# Run a language model

`lib/Llama.cs` runs small Llama-style language models in the formats of Andrej Karpathy's llama2.c:
a model file and a tokenizer file. `samples/Llama.cs` uses it with stories260K, a model of about
260,000 parameters trained on children's stories, and tells a story:

```bash
./build.sh Llama
./run.sh Llama                              # "Once upon a time", the likeliest word each time
./run.sh Llama -t 0.8 -s 7 -i "The dragon"  # a different start, words picked by chance
```

The program is about 1.1 MB, of which the model is 1 MB, and it needs no files: `build.sh` compiles
`models/stories260K.bin` and `models/tok512.bin` into it as byte literals. It runs on every target,
DOS on a 386 included (64 tokens in a few seconds of an emulated 386), and
[in your browser](../get-started/browser.html).

## Load the model

`Llama.Create` takes the model and tokenizer as byte spans and reads them in place. The weights are
never copied, and the run state (activations and the key/value cache) comes from an arena:

<!-- sample: samples/Llama.cs create -->
```csharp
Arena arena = Arena.Create(64 * 1024, "llama", stopOnOutOfMemory: true);
Llama llama = Llama.Create(LlamaModel.Model, LlamaModel.Tokenizer, arena);
```

The spans can be anything that stays put while the model is used. Here they are literals compiled
into the program; a file read into an arena works the same way.

## Generate text

Each step runs the model on one token at one position and gets a score (a logit) for every token in
the vocabulary. While the prompt lasts, the next token is the prompt's; after that, the sampler picks
one. `Decode` turns a token back into its UTF-8 bytes:

<!-- sample: samples/Llama.cs generate -->
```csharp
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
```

Generating allocates nothing: `Forward` works in the buffers `Create` took from the arena, and
`Decode` points into the tokenizer.

## Choose how words are picked

- **Greedy** (`-t 0`, the default): always the likeliest token. The same prompt gives the same story
  every run, on every target, which is what `test.sh` checks.
- **Temperature** (`-t 0.8`): divide the scores by the temperature and pick by chance. Lower is safer,
  higher more surprising.
- **Top p** (`-p 0.9`): with a temperature, pick only among the likeliest tokens whose probabilities
  add up to p, so the unlikely tail never comes up.

`-s` seeds the random numbers, so a sampled story can be repeated.

## How it fits together

The forward pass is llama2.c's: per layer, RMS normalization, attention with rotary position
encoding and grouped key/value heads, then a SwiGLU feed-forward block. The matrix-vector products,
`RmsNorm`, `Softmax` and `Silu` come from `lib/Kernels.cs`, with SIMD where the build's `-m` allows
(the sample is built like Simd and Kernels). The tokenizer turns text into tokens the same way as
llama2.c: a piece per character, falling back to bytes, then merging the best-scoring adjacent pair
until none is left.

The model's greedy story was checked token for token against an independent Python implementation
of the same algorithm.

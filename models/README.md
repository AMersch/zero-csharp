# Models

`stories260K.bin` and `tok512.bin` are Andrej Karpathy's smallest TinyStories
model (about 260,000 parameters) and its 512-token tokenizer, in llama2.c's
formats, from https://huggingface.co/karpathy/tinyllamas (`stories260K/`),
under the MIT license.

`samples/Llama.cs` compiles both into the program: `build.sh` turns them
into byte literals (`bin/gen/LlamaModel.cs`), so the program needs no files
when it runs, on any target.

open System

Out.Write "What's your name? "
let buffer = Array.zeroCreate<char> 64
let length = In.ReadLine(Span<char>(buffer))
Out.Write "Hello, "
if length <= 0 then Out.Write "world"
else Out.Write(ReadOnlySpan<char>(buffer, 0, min length buffer.Length))
Out.Line "!"

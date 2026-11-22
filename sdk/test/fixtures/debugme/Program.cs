// For the debugger test: locals to read at a breakpoint on the marked line.
using System;

int total = 0;
for (int i = 1; i <= 9; i++)
    total += i;
ReadOnlySpan<char> word = "zero";
Out.Line($"{total}"); // the breakpoint
Out.Line(word);

// Threads and the C math library, which a macOS program gets from libSystem:
// Parallel.For (pthreads), Kernels.Softmax (libm.so.6's expf) and MathF.Exp
// (zerocore's "*" import). Prints 499500, then 1.
using System;

public sealed class Box
{
    public long[] Results;
}

public static class Program
{
    public static unsafe int Main()
    {
        var box = new Box { Results = new long[1000] };
        Parallel.For(0, 1000, box, static (b, i) => b.Results[i] = i);
        long sum = 0;
        for (int i = 0; i < 1000; i++)
            sum += box.Results[i];
        Out.Write(sum);
        Out.Line();

        float* x = stackalloc float[1];
        x[0] = (float)(sum - 499500);
        Kernels.Softmax(x, 1);                              // 1
        Out.Write((long)(MathF.Exp(x[0] - 1f) + 0.5f));     // e^0 = 1
        Out.Line();
        return 0;
    }
}

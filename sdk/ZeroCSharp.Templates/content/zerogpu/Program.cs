// Two kernels on the GPU of an Apple Silicon Mac, checked against the CPU.
//
// A kernel is a static method marked [RuntimeExport("Name")], written in
// ordinary C#: pointers are buffers (GpuBuffer), other parameters are values
// passed with the launch, and Gpu.* gives the thread's position, threadgroup
// memory, barriers and atomics. Gpu.Run("Name", threads, arguments...) runs
// it. A kernel can't allocate, use strings or double, or call other methods.

using System;
using System.Runtime;

public static class Program
{
    const int N = 1 << 20;

    // y = a * x + y for the first n elements; threads past n do nothing.
    [RuntimeExport("Saxpy")]
    static unsafe void Saxpy(float* x, float* y, float a, int n)
    {
        uint i = Gpu.ThreadId();
        if (i < (uint)n)
            unsafe { y[i] = a * x[i] + y[i]; } // SAFETY: i < n, the buffers' length.
    }

    // Each threadgroup adds up its part of x in threadgroup memory, halving the
    // threads that work at each step, then adds its total to *total.
    [RuntimeExport("Sum")]
    static unsafe void Sum(int* x, int* total)
    {
        int* tile;
        unsafe { tile = (int*)Gpu.Shared(); } // SAFETY: the launch gives one int per thread.
        uint local = Gpu.LocalId();
        unsafe { tile[local] = x[Gpu.ThreadId()]; } // SAFETY: one thread per element.
        Gpu.Barrier();
        for (uint half = Gpu.GroupSize() / 2; half > 0; half /= 2)
        {
            if (local < half)
                unsafe { tile[local] += tile[local + half]; } // SAFETY: local + half < the group size.
            Gpu.Barrier();
        }
        if (local == 0)
            unsafe { Gpu.AtomicAdd(total, tile[0]); } // SAFETY: total is one int.
    }

    public static int Main()
    {
        if (!Gpu.Open())
        {
            Out.Line("No Metal GPU.");
            return 1;
        }
        Span<char> name = stackalloc char[64];
        Out.Line($"GPU: {Gpu.DeviceName(name)}");

        var x = GpuBuffer<float>.Allocate(N);
        var y = GpuBuffer<float>.Allocate(N);
        for (int i = 0; i < N; i++) { x.Span[i] = i % 100; y.Span[i] = 1; }
        double ms = Gpu.Run("Saxpy", N, x, y, 2f, N);
        int wrong = 0;
        for (int i = 0; i < N; i++)
            if (y.Span[i] != 2f * (i % 100) + 1) wrong++;
        Out.Line($"Saxpy: {N} elements, {(long)(ms * 1000)} us on the GPU, {wrong} wrong");

        // Threadgroups of 256 threads, each with 256 ints of threadgroup memory.
        const uint group = 256;
        var values = GpuBuffer<int>.Allocate(N);
        var total = GpuBuffer<int>.Allocate(1);
        int want = 0;
        for (int i = 0; i < N; i++) { values.Span[i] = i % 10; want += i % 10; }
        total.Span[0] = 0;
        ms = Gpu.Run("Sum", new GpuLaunch(N, group, (int)group * sizeof(int)), values, total);
        int got = total.Span[0];
        Out.Line($"Sum:   {got} (the CPU says {want}), {(long)(ms * 1000)} us on the GPU");

        x.Free(); y.Free(); values.Free(); total.Free();
        return wrong == 0 && got == want ? 0 : 1;
    }
}

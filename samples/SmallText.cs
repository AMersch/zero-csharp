// Test: what Out prints, sized for a small machine: whole numbers in every
// width and base, alignment and text, through $"..." and through Out.Write.
// Its output is compared with tests/expected character for character, on
// every target; on a Commodore 64 (./build.sh --c64) the 64-bit divisions
// behind it run on an 8-bit processor. SmallFloats.cs prints floating point.
// Build: ./build.sh SmallText

using System.Runtime.CompilerServices;

namespace SmallTextSample;

public static class Program
{
    // Hides a value from the optimizer, which would otherwise format the
    // constants at build time where it can.
    // Always false: see SmallIntegers.cs. It keeps a compiler that optimizes
    // the whole program at once from seeing through the method below.
    static bool s_never;
    [MethodImpl(MethodImplOptions.NoInlining)] static long L(long value) => s_never ? 0 : value;

    public static int Main()
    {
        s_never = false;
        int answer = (int)L(42);
        Out.Line($"int: {answer}, negative: {-answer}, zero: {(int)L(0)}");
        Out.Line($"limits: {(int)L(int.MinValue)} to {(int)L(int.MaxValue)}");
        Out.Line($"long: {L(long.MinValue)} to {L(long.MaxValue)}");
        Out.Line($"ulong: {(ulong)L(-1)}, uint: {(uint)L(4000000000)}");
        Out.Line($"small: {(byte)L(255)} {(sbyte)L(-128)} {(short)L(-32768)} {(ushort)L(65535)}");
        Out.Line($"hex: {answer:x} {255:X} {answer:x4} {L(0xDEADBEEF):x8} {L(-1):x}");
        Out.Line($"padded: [{answer,6}] [{answer,-6}] [{-answer,6}] [{answer:d5}]");
        Out.Line($"text: [{"left",-8}] [{"right",8}] {'c'} {true} {false}");

        Out.Write("Write: ");
        Out.Write(answer);
        Out.Write(' ');
        Out.Write(L(-9000000000));
        Out.Write(' ');
        Out.Write(true);
        Out.Line();
        return 0;
    }
}

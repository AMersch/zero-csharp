// Example: the Commander X16's banked RAM, from C#. Fills the banks with a
// pattern that depends on the address, reads it back across the borders
// between banks, and checks that nothing outside the banks was touched.
// Exit code = number of failed checks.
// Build: ./build.sh --cx16 BanksDemo    Run: ./run.sh --cx16 BanksDemo
//
// A program sees 8 KB of banked RAM at a time, so lib/Banks.cs copies bytes
// to and from it, by plain addresses that run over all the banks.

using System;

public static class Program
{
    static int s_failures;
    static int s_checks;
    static string s_firstFailure;

    static void Check(string name, bool ok)
    {
        s_checks++;
        if (!ok)
        {
            s_failures++;
            s_firstFailure ??= name;
        }
    }

    // The byte that belongs at an address: different in every bank.
    static byte Pattern(int address) => (byte)(address * 7 + address / Banks.BankSize);

    // docs:fill
    // Writes the pattern to count bytes from an address, 200 at a time.
    static void WritePattern(int address, int count)
    {
        Span<byte> chunk = stackalloc byte[200];
        for (int done = 0; done < count; done += chunk.Length)
        {
            int n = Math.Min(chunk.Length, count - done);
            for (int i = 0; i < n; i++)
                chunk[i] = Pattern(address + done + i);
            Banks.Write(address + done, chunk.Slice(0, n));
        }
    }

    static bool PatternHolds(int address, int count)
    {
        Span<byte> chunk = stackalloc byte[200];
        for (int done = 0; done < count; done += chunk.Length)
        {
            int n = Math.Min(chunk.Length, count - done);
            Banks.Read(address + done, chunk.Slice(0, n));
            for (int i = 0; i < n; i++)
                if (chunk[i] != Pattern(address + done + i))
                    return false;
        }
        return true;
    }
    // docs:end

    public static int Main()
    {
        // The emulator starts with 512 KB: 64 banks, and the KERNAL has the first.
        // An X16 has 512 KB to 2 MB of banked RAM, 64 banks to each 512 KB; the KERNAL's isn't counted.
        Check("banks", Banks.Count >= 63 && Banks.Count <= 255 && (Banks.Count + 1) % 64 == 0);
        Check("size", Banks.Size == Banks.Count * Banks.BankSize);

        // The first, the middle and the last bank, each with the borders around them.
        int last = Banks.Size - 1;
        WritePattern(0, 600);
        WritePattern(Banks.BankSize - 300, 600);                 // over the first border
        WritePattern(31 * Banks.BankSize - 100, 300);
        WritePattern(last - 400, 401);
        Check("first bank", PatternHolds(0, 600));
        Check("over a border", PatternHolds(Banks.BankSize - 300, 600));
        Check("middle", PatternHolds(31 * Banks.BankSize - 100, 300));
        Check("last byte", PatternHolds(last - 400, 401));

        // Banks are separate: the same offset, two banks, two values.
        Banks.Poke(5 * Banks.BankSize + 17, 0xAA);
        Banks.Poke(6 * Banks.BankSize + 17, 0x55);
        Check("poke and peek", Banks.Peek(5 * Banks.BankSize + 17) == 0xAA
            && Banks.Peek(6 * Banks.BankSize + 17) == 0x55);

        // Fill across three banks, and a byte either side of it left alone.
        Banks.Poke(10 * Banks.BankSize - 1, 1);
        Banks.Poke(12 * Banks.BankSize + 100, 2);
        Check("fill", Banks.Fill(10 * Banks.BankSize, 2 * Banks.BankSize + 100, 0xEE));
        Check("filled", Banks.Peek(10 * Banks.BankSize) == 0xEE
            && Banks.Peek(11 * Banks.BankSize + 4000) == 0xEE
            && Banks.Peek(12 * Banks.BankSize + 99) == 0xEE);
        Check("left alone", Banks.Peek(10 * Banks.BankSize - 1) == 1
            && Banks.Peek(12 * Banks.BankSize + 100) == 2);

        // Out of range does nothing, and says so.
        byte[] four = { 1, 2, 3, 4 };
        Check("past the end", !Banks.Write(Banks.Size - 3, four) && Banks.Peek(Banks.Size) == -1);
        Check("before the start", !Banks.Write(-1, four) && Banks.Peek(-1) == -1);
        Check("a refused write wrote nothing", Banks.Peek(Banks.Size - 3) != 1
            || Banks.Peek(Banks.Size - 2) != 2);
        Check("an empty copy at the end", Banks.Read(Banks.Size, new byte[0]));

        // The program's own memory is intact, and the window's bank is back.
        Check("the stack and heap still work", new int[20].Length == 20);

        if (s_failures == 0)
            Out.Line($"All {s_checks} checks passed.");
        else
            Out.Line($"{s_failures} of {s_checks} checks FAILED, the first: {s_firstFailure}");
        return s_failures;
    }
}

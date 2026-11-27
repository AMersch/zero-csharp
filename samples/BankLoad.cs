// Test: Banks.Load (lib/Banks.cs), which reads a file straight into the
// Commander X16's banked RAM with the KERNAL's LOAD. The files are written
// with File here, so that the program needs nothing but itself; a program
// that only loads doesn't carry that code. Exit code = number of failed
// checks.
//
// Build: ./build.sh --cx16 BankLoad

using System;

public static class Program
{
    const string Small = "load.tmp";
    const string Large = "large.tmp";

    static int s_checks;
    static int s_failures;
    static string s_first = "";

    static void Check(string name, bool ok)
    {
        s_checks++;
        if (!ok && s_failures++ == 0)
            s_first = name;
    }

    // The byte a file made here has at an index: no two neighbouring KB alike.
    static byte At(int i) => (byte)(i * 7 + i / 256);

    static bool Make(string path, int length)
    {
        File file = File.Create(path);
        if (!file.IsOpen)
            return false;
        Span<byte> chunk = stackalloc byte[100];
        bool ok = true;
        for (int done = 0; done < length; done += chunk.Length)
        {
            Span<byte> part = chunk.Slice(0, Math.Min(chunk.Length, length - done));
            for (int i = 0; i < part.Length; i++)
                part[i] = At(done + i);
            ok &= file.Write(part) == part.Length;
        }
        unsafe { file.Close(); }
        return ok;
    }

    static int ByteAt(int address)
    {
        Span<byte> one = stackalloc byte[1];
        return Banks.Read(address, one) ? one[0] : -1;
    }

    static bool Holds(int address, int length)
    {
        Span<byte> chunk = stackalloc byte[100];
        for (int done = 0; done < length; done += chunk.Length)
        {
            Span<byte> part = chunk.Slice(0, Math.Min(chunk.Length, length - done));
            if (!Banks.Read(address + done, part))
                return false;
            for (int i = 0; i < part.Length; i++)
                if (part[i] != At(done + i))
                    return false;
        }
        return true;
    }

    public static int Main()
    {
        Check("the files are written", Make(Small, 300) && Make(Large, 20000));
        int bank = CX16.Peek(0);

        Banks.Fill(0, 400, 0xEE);
        Check("a small file's length", Banks.Load(0, Small) == 300);
        Check("its bytes, from the first one on", Holds(0, 300));
        Check("nothing after them", ByteAt(300) == 0xEE);

        Check("a file longer than two banks", Banks.Load(100, Large) == 20000);
        Check("its bytes", Holds(100, 20000));

        Check("into the end of a bank and on", Banks.Load(Banks.BankSize - 10, Small) == 300);
        Check("its bytes", Holds(Banks.BankSize - 10, 300));

        Check("a file that ends where a bank does", Banks.Load(3 * Banks.BankSize - 300, Small) == 300);
        Check("its bytes", Holds(3 * Banks.BankSize - 300, 300));

        File open = File.OpenRead(Small);
        Span<byte> one = stackalloc byte[1];
        open.Read(one);
        Check("with a file open", Banks.Load(0, Large) == 20000 && Holds(0, 20000));
        Check("which reads on", open.Read(one) == 1 && one[0] == At(1));
        unsafe { open.Close(); }

        Check("a file that isn't there", Banks.Load(0, "nothing.tmp") == -1);
        Check("an address before the banks", Banks.Load(-1, Small) == -1);
        Check("an address after them", Banks.Load(Banks.Size, Small) == -1);
        Check("no name", Banks.Load(0, "") == -1);
        Check("and it still loads", Banks.Load(0, Small) == 300);
        Check("the RAM bank is as before", CX16.Peek(0) == bank);

        File.Delete(Small);
        File.Delete(Large);
        if (s_failures == 0)
            Out.Line("All " + s_checks.ToString() + " checks passed.");
        else
            Out.Line(s_failures.ToString() + " of " + s_checks.ToString() + " checks FAILED, the first: " + s_first);
        return s_failures;
    }
}

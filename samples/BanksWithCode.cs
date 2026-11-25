// Test: a Commander X16 program with code in RAM banks that also uses the
// banked RAM for data. The double arithmetic and formatting are more code than
// the X16's 38 KB of fixed RAM hold, so the build moves some of it into banks
// (sdk/ZeroCSharp.Bitcode/X16.cs); lib/Banks.cs then has the banks after
// those. The test fills all of them with a pattern, and then calls the
// moved functions again: if the data had overwritten the code, they would
// crash. Exit code = number of failed checks.
// Build: ./build.sh --cx16 BanksWithCode

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

    // Some code that is big in a program with floating point in it.
    static string Show(double value) => value.ToString();

    public static int Main()
    {
        Check("formatting before", Show(0.1 + 0.2) == "0.30000000000000004" && Show(2.5) == "2.5");

        // Some of the program is in banks 1 to n, so there are fewer for data.
        Check("data banks start after the code", Banks.Count < 63 && Banks.Count >= 40);

        // 3,000 doubles: 24,000 bytes, over four banks.
        BankedArray<double> roots = BankedArray<double>.Create(3000);
        for (int i = 0; i < roots.Length; i++)
            roots[i] = Math.Sqrt(i);
        double sum = 0;
        for (int i = 0; i < roots.Length; i++)
            sum += roots[i];
        Check("a sum over the banks", sum > 109516.9 && sum < 109516.95);
        Check("a value", roots[2500] == 50);

        // Everything the data can reach, overwritten.
        Check("fill", Banks.Fill(0, Banks.Size, 0xEE) && Banks.Peek(Banks.Size - 1) == 0xEE);
        BankedList<int> list = BankedList<int>.Create(8);
        for (int i = 0; i < 1000; i++)
            list.Add(i * 3);
        Check("a list", list.Count == 1000 && list[999] == 2997);

        // The moved code still works.
        Check("formatting after", Show(1.0 / 3) == "0.3333333333333333" && Show(-1234.5) == "-1234.5");

        if (s_failures == 0)
            Out.Line($"All {s_checks} checks passed.");
        else
            Out.Line($"{s_failures} of {s_checks} checks FAILED, the first: {s_firstFailure}");
        return s_failures;
    }
}

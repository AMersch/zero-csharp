// Fuzzes zerocore's decimal (System/Decimal.cs, renamed ZDecimal by
// fuzz-decimal.sh) against .NET's: every operation's bits, or the exception
// type, must match. Usage: fuzz-decimal.sh [iterations] [seed]
using System;
using System.Globalization;
using ZeroTest;

static class Program
{
    static Random R = new Random(12345);
    static int failures;
    static int checks;

    static (decimal, ZDecimal) RandomPair()
    {
        int kind = R.Next(12);
        if (kind == 10)
        {
            // Edges: the extremes, one, and the smallest step.
            switch (R.Next(6))
            {
                case 0: return (decimal.MaxValue, ZDecimal.MaxValue);
                case 1: return (decimal.MinValue, ZDecimal.MinValue);
                case 2: return (decimal.One, ZDecimal.One);
                case 3: return (decimal.MinusOne, ZDecimal.MinusOne);
                case 4: return (new decimal(1, 0, 0, false, 28), new ZDecimal(1, 0, 0, false, 28));
                default: { bool s = R.Next(2) == 0; return (new decimal(-1, -1, -1, s, 28), new ZDecimal(-1, -1, -1, s, 28)); }
            }
        }
        int lo = 0, mid = 0, hi = 0;
        int limbs = R.Next(4);
        if (kind == 0) { lo = R.Next(0, 1000); }
        else
        {
            if (limbs >= 1) lo = R.Next(int.MinValue, int.MaxValue);
            if (limbs >= 2) mid = R.Next(int.MinValue, int.MaxValue);
            if (limbs >= 3) hi = R.Next(int.MinValue, int.MaxValue) >> R.Next(0, 32);
        }
        bool neg = R.Next(2) == 0;
        byte scale = (byte)(kind == 1 ? 0 : R.Next(0, 29));
        if (kind == 2) scale = (byte)R.Next(0, 4);
        return (new decimal(lo, mid, hi, neg, scale), new ZDecimal(lo, mid, hi, neg, scale));
    }

    static string Bits(decimal d)
    {
        int[] b = decimal.GetBits(d);
        return $"{b[0]:x8} {b[1]:x8} {b[2]:x8} {b[3]:x8} ({d.ToString(CultureInfo.InvariantCulture)})";
    }

    static string Bits(ZDecimal d)
    {
        Span<int> b = stackalloc int[4];
        ZDecimal.GetBits(d, b);
        return $"{b[0]:x8} {b[1]:x8} {b[2]:x8} {b[3]:x8} ({d})";
    }

    static void Check(string what, Func<string> expected, Func<string> actual)
    {
        checks++;
        string e, a;
        try { e = expected(); } catch (Exception ex) { e = ex.GetType().Name; }
        try { a = actual(); } catch (Exception ex) { a = ex.GetType().Name; }
        if (e != a)
        {
            failures++;
            string op = what.Split(' ')[0];
            ByOp.TryGetValue(op, out int seen);
            ByOp[op] = seen + 1;
            if (seen < 3)
                Console.WriteLine($"{what}\n  .NET: {e}\n  zero: {a}");
        }
    }

    static readonly System.Collections.Generic.Dictionary<string, int> ByOp = new();

    static int Main(string[] args)
    {
        int n = args.Length > 0 ? int.Parse(args[0]) : 200000;
        if (args.Length > 1) R = new Random(int.Parse(args[1]));
        for (int i = 0; i < n; i++)
        {
            var (a, za) = RandomPair();
            var (b, zb) = RandomPair();
            string ab = $"{Bits(a)} , {Bits(b)}";
            Check("add " + ab, () => Bits(a + b), () => Bits(za + zb));
            Check("sub " + ab, () => Bits(a - b), () => Bits(za - zb));
            Check("mul " + ab, () => Bits(a * b), () => Bits(za * zb));
            Check("div " + ab, () => Bits(a / b), () => Bits(za / zb));
            Check("rem " + ab, () => Bits(a % b), () => Bits(za % zb));
            Check("cmp " + ab, () => decimal.Compare(a, b).ToString(), () => ZDecimal.Compare(za, zb).ToString());
            Check("str " + Bits(a), () => a.ToString(CultureInfo.InvariantCulture), () => za.ToString());
            int k = R.Next(0, 29);
            Check($"round{k} " + Bits(a), () => Bits(decimal.Round(a, k)), () => Bits(ZDecimal.Round(za, k)));
            Check("floor " + Bits(a), () => Bits(decimal.Floor(a)), () => Bits(ZDecimal.Floor(za)));
            Check("ceil " + Bits(a), () => Bits(decimal.Ceiling(a)), () => Bits(ZDecimal.Ceiling(za)));
            Check("trunc " + Bits(a), () => Bits(decimal.Truncate(a)), () => Bits(ZDecimal.Truncate(za)));
            Check("todouble " + Bits(a), () => ((double)a).ToString("R", CultureInfo.InvariantCulture), () => ((double)za).ToString("R", CultureInfo.InvariantCulture));
            Check("tolong " + Bits(a), () => ((long)a).ToString(), () => ((long)za).ToString());
            Check("toint " + Bits(a), () => ((int)a).ToString(), () => ((int)za).ToString());
            Check("parse " + Bits(a), () => Bits(decimal.Parse(a.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)),
                () => Bits(ZDecimal.Parse(za.ToString())));
            Check("hash " + ab, () => (a == b) ? "eq" : "ne",
                () => (za == zb) ? (za.GetHashCode() == zb.GetHashCode() ? "eq" : "HASH-DIFFERS") : "ne");
            double dv = RandomDouble();
            Check("fromdouble " + dv.ToString("R", CultureInfo.InvariantCulture), () => Bits((decimal)dv), () => Bits((ZDecimal)dv));
            long lv = R.NextInt64(long.MinValue, long.MaxValue) >> R.Next(0, 63);
            Check("fromlong " + lv, () => Bits((decimal)lv), () => Bits((ZDecimal)lv));
            float fv = (float)RandomDouble();
            Check("fromfloat " + fv.ToString("R", CultureInfo.InvariantCulture), () => Bits((decimal)fv), () => Bits((ZDecimal)fv));
            Check("tofloat " + Bits(a), () => ((float)a).ToString("R", CultureInfo.InvariantCulture), () => ((float)za).ToString("R", CultureInfo.InvariantCulture));
            string text = RandomText();
            Check("parsetext " + text, () => Bits(decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)),
                () => Bits(ZDecimal.Parse(text)));
        }
        // Equal values with different scales, and zeros.
        Check("hash 1.0/1.00", () => "eq", () => new ZDecimal(10, 0, 0, false, 1).GetHashCode() == new ZDecimal(100, 0, 0, false, 2).GetHashCode() ? "eq" : "ne");
        foreach (var kv in ByOp)
            Console.WriteLine($"  {kv.Key}: {kv.Value}");
        Console.WriteLine($"{checks} checks, {failures} failures");
        return failures == 0 ? 0 : 1;
    }

    static double RandomDouble()
    {
        switch (R.Next(5))
        {
            case 0: return Math.Round(R.NextDouble() * 1000, R.Next(0, 6)) * (R.Next(2) == 0 ? -1 : 1);
            case 1: return R.NextDouble() * Math.Pow(10, R.Next(-30, 30));
            case 2: return R.Next(-100000, 100000) / 100.0;
            case 3: return BitConverter.Int64BitsToDouble(R.NextInt64());
            default: return R.NextDouble();
        }
    }

    static string RandomText()
    {
        var sb = new System.Text.StringBuilder();
        if (R.Next(3) == 0) sb.Append('-');
        int intDigits = R.Next(0, 32);
        for (int i = 0; i < intDigits; i++) sb.Append((char)('0' + R.Next(10)));
        if (R.Next(2) == 0)
        {
            sb.Append('.');
            int frac = R.Next(0, 35);
            for (int i = 0; i < frac; i++) sb.Append((char)('0' + R.Next(10)));
        }
        if (sb.Length == 0 || sb.ToString() == "-" || sb.ToString().EndsWith(".") && sb.Length == 1) sb.Append('0');
        if (R.Next(6) == 0) sb.Append('e').Append(R.Next(-30, 30));
        return sb.ToString();
    }
}

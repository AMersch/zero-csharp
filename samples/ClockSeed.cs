// Test: the Commander X16's real-time clock and its random bits. Clock.UtcNow
// reads the real-time clock (once, then it counts jiffies), CX16.SetDateTime sets
// it, Clock.Monotonic starts at a random moment, and CX16.Entropy gives 24
// random bits. This sets the machine's clock, to dates in the 2020s and 2030s,
// and puts the time back at the end (to the second: the X16's clock has no more).
// The emulator is started with -rtc, which gives it the computer's time.
// Exit code = number of failed checks.
// Build: ./build.sh --cx16 ClockSeed

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

    public static int Main()
    {
        // What the clock says at the start, to put back at the end.
        CalendarTime started = Clock.UtcNow;
        // (A real X16 whose clock has never been set starts at 2000.)
        Check("the year is in this century", started.Year >= 2000 && started.Year < 2100);
        Check("the other fields are in range", started.Month >= 1 && started.Month <= 12 && started.Day >= 1 && started.Day <= 31
            && started.Hour < 24 && started.Minute < 60 && started.Second < 60);

        long first = Clock.UnixMilliseconds, second = Clock.UnixMilliseconds;
        Check("the wall clock doesn't go back", second >= first);

        long m1 = Clock.Monotonic, m2 = Clock.Monotonic;
        Check("the monotonic clock starts somewhere and doesn't go back", m1 > 0 && m2 >= m1);

        // Setting it.
        CX16.SetDateTime(2031, 2, 3, 4, 5, 6);
        CalendarTime set = Clock.UtcNow;
        Check("the date that was set", set.Year == 2031 && set.Month == 2 && set.Day == 3);
        Check("the time that was set", set.Hour == 4 && set.Minute == 5 && set.Second >= 6 && set.Second <= 7);
        Check("a Monday", set.DayOfWeek == 1);
        long ms = Clock.UnixMilliseconds;
        Check("as Unix time", ms >= 1927857906000L && ms < 1927857908000L);

        CX16.SetDateTime(2024, 2, 29, 23, 59, 59);
        CalendarTime leap = Clock.UtcNow;
        Check("a leap day", leap.Year == 2024 && leap.Month == 2 && leap.Day == 29 && leap.Hour == 23);
        Check("a Thursday", leap.DayOfWeek == 4);

        CX16.SetDateTime(2031, 13, 3, 0, 0, 0);
        CX16.SetDateTime(1800, 1, 1, 0, 0, 0);
        CX16.SetDateTime(2031, 1, 1, 24, 0, 0);
        Check("dates that don't exist change nothing", Clock.UtcNow.Year == 2024);

        // The random bits.
        int a = CX16.Entropy, b = CX16.Entropy, c = CX16.Entropy, d = CX16.Entropy;
        Check("24 bits", (uint)a < 0x1000000 && (uint)b < 0x1000000 && (uint)c < 0x1000000);
        Check("they differ", !(a == b && b == c && c == d));
        Random rng = new Random((ulong)CX16.Entropy);
        Check("a Random from them works", rng.Next(100) < 100);

        // Put the time back.
        CX16.SetDateTime(started.Year, started.Month, started.Day, started.Hour, started.Minute, started.Second);

        if (s_failures == 0)
            Out.Line("All " + s_checks.ToString() + " checks passed.");
        else
            Out.Line(s_failures.ToString() + " of " + s_checks.ToString() + " checks FAILED, the first: " + s_firstFailure);
        return s_failures;
    }
}

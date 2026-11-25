// A microcontroller program (ZeroTarget=pico2 or r4): a C# interrupt handler
// counts timer ticks and turns the LED over, and the program says how it went.
using System.Runtime.InteropServices;

public static class Program
{
    static int s_ticks;

    [UnmanagedCallersOnly]
    static void OnTick()
    {
        s_ticks++;
        Board.Led(!Board.LedIsOn);
    }

    public static int Main()
    {
        Board.Led(false);
        unsafe
        {
            if (!Board.StartTicks(100, &OnTick))
                return 2;
        }
        while (s_ticks < 10)
            Board.WaitForInterrupt();
        Board.StopTicks();
        Out.Line($"ticks: {s_ticks}, LED on: {Board.LedIsOn}");
        return s_ticks == 10 && !Board.LedIsOn ? 0 : 1;
    }
}

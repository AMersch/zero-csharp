// A microcontroller board: its LED, the chip's hardware registers, and a
// timer that calls C# a number of times a second. Only for programs built
// with ./build.sh --pico2 or --r4; the functions are in arm/runtime.c and
// the board's own file (arm/rp2350.c on a real Pico 2, arm/semihost.c in
// QEMU), and a program for another target that calls them won't link.
//
//   Board.Led(true);
//   unsafe { Board.StartTicks(10, &OnTick); }   // OnTick: [UnmanagedCallersOnly]
//   while (s_ticks < 50)
//       Board.WaitForInterrupt();                // sleeps until the next tick
//   Board.StopTicks();
//
//   uint id = Board.Read(0x40000000);           // any register at all
//   Board.Write(0xd0000018, 1u << 25);          // on a Pico 2: GP25 high
//
// A tick handler runs between any two instructions of the rest of the
// program, so it should be short: count, set a flag, toggle a pin. It may
// call Board and change static fields; it must not allocate, print, or use
// an arena the main program uses. A static field the two share is best read
// once per loop turn into a local.

using System.Runtime.InteropServices;

public static class Board
{
    [DllImport("libc")] static safe extern uint zero_read32(nuint address);
    [DllImport("libc")] static safe extern void zero_write32(nuint address, uint value);
    [DllImport("libc")] static safe extern void zero_led(int on);
    [DllImport("libc")] static safe extern int zero_led_is_on();
    [DllImport("libc")] static safe extern uint zero_cpu_hz();
    [DllImport("libc")] static safe extern void zero_wait_for_interrupt();
    [DllImport("libc")] static unsafe extern int zero_tick_start(int hz, delegate* unmanaged<void> handler);
    [DllImport("libc")] static safe extern void zero_tick_stop();

    /// <summary>Turns the board's LED on or off: the green one on a Pico 2, the
    /// first of the FPGA's LEDs on QEMU's boards.</summary>
    public static void Led(bool on) => zero_led(on ? 1 : 0);

    /// <summary>Whether the LED is on.</summary>
    public static bool LedIsOn => zero_led_is_on() != 0;

    /// <summary>
    /// The 32-bit hardware register at <paramref name="address"/>, read once,
    /// right here: the compiler never merges, moves or skips the read, as it
    /// may with a read through a pointer.
    /// </summary>
    public static uint Read(nuint address) => zero_read32(address);

    /// <summary>Writes a 32-bit hardware register, once, right here.</summary>
    public static void Write(nuint address, uint value) => zero_write32(address, value);

    /// <summary>The processor's clock, in cycles per second.</summary>
    public static uint ClockHz => zero_cpu_hz();

    /// <summary>
    /// Calls <paramref name="handler"/> <paramref name="hz"/> times a second,
    /// from the SysTick timer's interrupt, until StopTicks. The handler is a
    /// static method marked [UnmanagedCallersOnly], with no parameters.
    /// A throw in it ends the program, also when the code it interrupted
    /// is inside a Catch.Run; a Catch.Run in the handler itself works.
    /// Returns false if the timer can't count that rate.
    /// </summary>
    public static unsafe bool StartTicks(int hz, delegate* unmanaged<void> handler)
    {
        // SAFETY: the caller passes a static [UnmanagedCallersOnly] method,
        // which lives as long as the program.
        unsafe { return zero_tick_start(hz, handler) == 0; }
    }

    /// <summary>Stops the ticks.</summary>
    public static void StopTicks() => zero_tick_stop();

    /// <summary>Sleeps until an interrupt (a tick, for one) has been handled.</summary>
    public static void WaitForInterrupt() => zero_wait_for_interrupt();
}

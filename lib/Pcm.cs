// Sampled sound on the Commander X16: the video chip's PCM channel. Only for programs
// built with ./build.sh --cx16; the functions are in cx16/runtime.c, and a program for
// another target that calls them won't link.
//
//   byte[] beep = new byte[2000];
//   for (int i = 0; i < beep.Length; i++)
//       beep[i] = (byte)((i & 16) != 0 ? 60 : -60);        // signed samples
//   Pcm.Play(beep, 8000);                                  // returns at once
//   while (Pcm.IsPlaying) { /* do something else */ }
//
// The sound plays in the background: an interrupt puts the next bytes into the chip's
// 4 KB buffer when it runs low, so there is nothing to call while it plays. There is one
// channel; Play ends what was playing.
//
// The samples are signed: 0 is silence, 8-bit ones go from -128 to 127 (in a byte, 128
// to 255 are the negative half). A WAV file's 8-bit samples are unsigned: take 128 off
// each (flip the top bit). 16-bit samples are the low byte first, stereo is left before
// right. The rate is in Hz, up to 48828; the chip has steps of 381 Hz.
//
// The array is read while the sound plays: don't change it, and don't let go of an Arena
// it was made in. For sounds that don't fit in the program's memory, put them in the
// banked RAM (Banks.Write, a piece at a time from a File) and use PlayBanked.
//
// The buffer is filled up when 1 KB is left in it, so an interrupt that comes that much
// too late is heard as a gap: 1 KB lasts an eighth of a second at 8000 Hz mono 8-bit, but
// a two-hundredth at 48828 Hz stereo 16-bit. Interrupts are off while the KERNAL loads
// from disk and during some drawing (Bitmap.Clear with VERA FX: a sixtieth of a second).
// Measured, the sound takes this much of the processor: 2% at 8000 Hz mono 8-bit, 4% at
// 16000, 10% at 22050 Hz mono 16-bit, 46% at 48828 Hz stereo 16-bit.

using System;
using System.Runtime.InteropServices;

public static class Pcm
{
    [DllImport("libc")] static unsafe extern int cx16_pcm_play(byte* data, int length, int rate, int format, int loop);
    [DllImport("libc")] static safe extern int cx16_pcm_play_banked(int address, int length, int rate, int format, int loop);
    [DllImport("libc")] static safe extern void cx16_pcm_stop();
    [DllImport("libc")] static safe extern int cx16_pcm_left();
    [DllImport("libc")] static safe extern int cx16_pcm_playing();
    [DllImport("libc")] static safe extern void cx16_pcm_volume(int volume);

    static byte[] s_playing;        // kept: the interrupt reads it

    // The chip's rate: 128 is 48828 Hz.
    static int Step(int hertz)
    {
        if (hertz <= 0 || hertz > 48828)
            return 0;
        int step = (hertz * 128 + 24414) / 48828;
        return step < 1 ? 1 : step;
    }

    static int Format(bool sixteenBit, bool stereo) => (sixteenBit ? 1 : 0) | (stereo ? 2 : 0);

    /// <summary>
    /// Plays signed samples from an array, in the background, once or again and again. False, and nothing changes,
    /// for a rate that isn't 1 to 48828 Hz or an array without one whole sample.
    /// </summary>
    public static bool Play(byte[] samples, int rate, bool sixteenBit = false, bool stereo = false, bool loop = false)
    {
        if (samples == null || samples.Length == 0 || Step(rate) == 0 || samples.Length < (sixteenBit ? 2 : 1) * (stereo ? 2 : 1))
            return false;
        cx16_pcm_stop();
        s_playing = samples;
        unsafe
        {
            // SAFETY: the array stays alive in s_playing and managed memory doesn't move; the length is its own.
            fixed (byte* p = samples)
                return cx16_pcm_play(p, samples.Length, Step(rate), Format(sixteenBit, stereo), loop ? 1 : 0) != 0;
        }
    }

    /// <summary>
    /// As Play, for count bytes of the banked RAM from an address as Banks counts them.
    /// False for bytes outside the banks. The program must not write to them while they play.
    /// </summary>
    public static bool PlayBanked(int address, int count, int rate, bool sixteenBit = false, bool stereo = false, bool loop = false)
    {
        if (address < 0 || count <= 0 || count > Banks.Size - address || Step(rate) == 0)
            return false;
        return cx16_pcm_play_banked(address, count, Step(rate), Format(sixteenBit, stereo), loop ? 1 : 0) != 0;
    }

    /// <summary>Ends the sound at once.</summary>
    public static void Stop() => cx16_pcm_stop();

    /// <summary>Whether there is still something to hear.</summary>
    public static bool IsPlaying => cx16_pcm_playing() != 0;

    /// <summary>The bytes that haven't been handed to the chip yet, of this round for a loop; the chip holds up to 4 KB more.</summary>
    public static int Remaining => cx16_pcm_left();

    /// <summary>How loud, from 0 (silent) to 15 (the loudest, and the start). The steps are logarithmic.</summary>
    public static int Volume { set => cx16_pcm_volume(value); }
}

// Example and test: sampled sound on the Commander X16 (lib/Pcm.cs): a square wave from 8-bit samples, a sine
// from 16-bit samples, one speaker of a stereo sound, a long sound from the banked RAM, a loop, and two volumes.
// About 8 seconds. ./test.sh --cx16 runs it through with no sound (the emulator plays none without a screen,
// and its buffer never empties) and checks what Pcm answers and what it tells the chip; ./test.sh --cx16-audio
// records it and measures every sound. Exit code = number of failed checks.
// Build: ./build.sh --cx16 PcmDemo    Run: ./run.sh --cx16 --window PcmDemo

using System;

public static class Program
{
    static void Wait(int frames)
    {
        for (int i = 0; i < frames; i++)
            CX16.WaitFrame();
    }

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

    static int Format => CX16.Peek(0x9F3B) & 0x3F;          // 16-bit, stereo and the volume
    static bool Interrupting => (CX16.Peek(0x9F26) & 8) != 0;

    public static int Main()
    {
        CX16.Clear(CX16.Black);
        CX16.Print(2, 2, "PCM: listen", CX16.Yellow);

        // A square wave, 20 samples a period, 8 bits: at 8000 Hz (the chip's nearest is 8011) 400.5 Hz.
        byte[] square = new byte[6000];
        for (int i = 0; i < square.Length; i++)
            square[i] = (byte)(i % 20 < 10 ? 50 : -50);

        // A sine, 32 samples a period, 16 bits: at 24414 Hz 762.9 Hz.
        byte[] sine = new byte[2 * 32];
        for (int i = 0; i < 32; i++)
        {
            int value = CX16.Sin(i * 8) / 2;
            sine[2 * i] = (byte)value;
            sine[2 * i + 1] = (byte)(value >> 8);
        }

        // The same square in the left speaker only: 8-bit stereo.
        byte[] left = new byte[2000];               // 50 periods, played in a loop
        for (int i = 0; i < left.Length; i += 2)
            left[i] = square[i / 2];

        // 40,000 samples of a sine of 64 a period in the banks, across five of them: at 24414 Hz 381.5 Hz, 1.64 s.
        const int Long = 40000, Piece = 1024;
        byte[] piece = new byte[Piece];
        for (int i = 0; i < Piece; i++)
            piece[i] = (byte)(CX16.Sin(i * 4) >> 9);
        for (int at = 0; at < Long; at += Piece)
            Banks.Write(100 + at, piece);

        CX16.StartRecording();                  // in the emulator started with -wav: record from here

        // What Pcm refuses.
        Check("rates that aren't", !Pcm.Play(square, 0) && !Pcm.Play(square, -1) && !Pcm.Play(square, 48829));
        Check("no whole sample", !Pcm.Play(new byte[0], 8000) && !Pcm.Play(new byte[1], 8000, sixteenBit: true)
            && !Pcm.Play(new byte[3], 8000, sixteenBit: true, stereo: true));
        Check("outside the banks", !Pcm.PlayBanked(-1, 100, 8000) && !Pcm.PlayBanked(Banks.Size - 50, 100, 8000) && !Pcm.PlayBanked(0, 0, 8000));
        Check("nothing is playing", !Pcm.IsPlaying && Pcm.Remaining == 0);

        // 1. 8 bits.
        CX16.Print(2, 4, "1. square, 8 bits, 400 Hz", CX16.White);
        Check("play", Pcm.Play(square, 8000));
        int sent = square.Length - Pcm.Remaining;
        Check("the buffer is filled first", sent >= 3584 && sent <= 3839);
        Check("playing, with the interrupt", Pcm.IsPlaying && Interrupting && Format == 0x0F);
        Wait(60);                               // 6000 samples: 0.75 s
        Wait(30);

        // 2. 16 bits, a loop of one period.
        CX16.Print(2, 5, "2. sine, 16 bits, 763 Hz", CX16.White);
        Check("play 16 bits in a loop", Pcm.Play(sine, 24414, sixteenBit: true, loop: true));
        Check("16 bits", Pcm.IsPlaying && Interrupting && Format == 0x2F);
        Wait(60);
        Pcm.Stop();
        Check("stop", !Pcm.IsPlaying && !Interrupting && Pcm.Remaining == 0);
        Wait(30);

        // 3. Stereo.
        CX16.Print(2, 6, "3. left speaker only", CX16.White);
        Check("play stereo", Pcm.Play(left, 8000, stereo: true, loop: true) && Format == 0x1F);
        Wait(60);
        Pcm.Stop();
        Wait(30);

        // 4. From the banks.
        CX16.Print(2, 7, "4. from the banked RAM, 381 Hz", CX16.White);
        Check("play from the banks", Pcm.PlayBanked(100, Long, 24414));
        sent = Long - Pcm.Remaining;
        Check("from the banks", sent >= 3584 && sent <= 3839 && Format == 0x0F);
        Wait(130);                              // 1.64 s, and a pause

        // 5. and 6. Two volumes.
        CX16.Print(2, 8, "5. and 6. loud, then quiet", CX16.White);
        Pcm.Play(square, 8000, loop: true);
        Wait(60);
        Pcm.Stop();
        Wait(30);
        Pcm.Volume = 7;
        Pcm.Play(square, 8000, loop: true);
        Check("volume", Format == 0x07);
        Wait(60);
        Pcm.Stop();
        Pcm.Volume = 15;
        Wait(30);

        // A sound short enough to go into the buffer whole needs no interrupt.
        Check("a short sound", Pcm.Play(new byte[1000], 8000) && Pcm.Remaining == 0 && !Interrupting);
        Pcm.Stop();

        // Left playing: the end of the program ends it.
        Pcm.Play(square, 8000, loop: true);
        Check("a loop goes on", Pcm.IsPlaying && Interrupting);
        Pcm.Stop();

        CX16.Clear(CX16.Blue);
        if (s_failures == 0)
            Out.Line("All " + s_checks.ToString() + " checks passed.");
        else
            Out.Line(s_failures.ToString() + " of " + s_checks.ToString() + " checks FAILED, the first: " + s_firstFailure);
        return s_failures;
    }
}

// Example and test: sound on the Commander X16: a pure tone from the video chip's sound generator, notes from
// the FM chip (the YM2151) on several channels, an organ, a chord, a note in one speaker, two volumes and a fine
// tuning. About 14 seconds. ./test.sh --cx16 only runs it through, with no sound (the emulator makes none
// without a screen) and checks what Fm answers for a channel, a note or an instrument that doesn't exist;
// ./test.sh --cx16-audio (cx16/test-audio.sh) records it in the emulator and measures every note's pitch,
// partials, speaker and volume. Exit code = number of failed checks.
// Build: ./build.sh --cx16 SoundDemo    Run: ./run.sh --cx16 --window SoundDemo

using System;

public static class Program
{
    // The program's own clock: n frames, a sixtieth of a second each. (WaitFrame ends by itself in the
    // emulator's headless mode, which has no frames; then the whole is over in a moment.)
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

    public static int Main()
    {
        CX16.Clear(CX16.Black);
        CX16.Print(2, 2, "Sound: listen", CX16.Yellow);
        CX16.StartRecording();                  // in the emulator started with -wav: record from here
        Fm.Reset();
        Wait(30);

        // 1. The video chip's sound generator: a triangle wave of 440 Hz, voice 0.
        CX16.Print(2, 4, "1. PSG triangle, 440 Hz", CX16.White);
        CX16.Tone(0, 440, 40, CX16.Triangle);
        Wait(60);
        CX16.Silence(0);
        Wait(30);

        // 2. The FM chip: the same pitch as a pure tone, from MIDI note 69.
        CX16.Print(2, 5, "2. FM sine, A 440 Hz", CX16.White);
        Fm.SetPatch(0, Fm.Sine);
        Fm.NoteOn(0, 69);
        Wait(60);
        Fm.NoteOff(0);
        Wait(30);

        // 3. An organ an octave and a half lower: A 220 Hz, with partials above it.
        CX16.Print(2, 6, "3. FM organ, A 220 Hz", CX16.White);
        Fm.SetPatch(1, Fm.Organ);
        Fm.NoteOn(1, 57);
        Wait(60);
        Fm.NoteOff(1);
        Wait(30);

        // 4. Two channels at once: A and the E above it, a fifth.
        CX16.Print(2, 7, "4. a fifth: 440 and 659 Hz", CX16.White);
        Fm.SetPatch(2, Fm.Sine);
        Fm.SetPatch(3, Fm.Sine);
        Fm.NoteOn(2, 69);
        Fm.NoteOn(3, 76);
        Wait(60);
        Fm.NoteOff(2);
        Fm.NoteOff(3);
        Wait(30);

        // 5. A note in the left speaker only.
        CX16.Print(2, 8, "5. left speaker only", CX16.White);
        Fm.SetPatch(4, Fm.Sine);
        Fm.SetPan(4, left: true, right: false);
        Fm.NoteOn(4, 69);
        Wait(60);
        Fm.NoteOff(4);
        Wait(30);

        // 6. The same note at full volume and at 100 of 127: 27 steps of 0.75 dB quieter.
        CX16.Print(2, 9, "6. louder, then quieter", CX16.White);
        Fm.SetPatch(5, Fm.Sine);
        Fm.NoteOn(5, 69);
        Wait(60);
        Fm.NoteOff(5);
        Wait(30);
        Fm.SetVolume(5, 100);
        Fm.NoteOn(5, 69);
        Wait(60);
        Fm.NoteOff(5);
        Wait(30);

        // 7. A fine tuning: the same note half a semitone up (32 of 64) is 452.9 Hz.
        CX16.Print(2, 10, "7. half a semitone up", CX16.White);
        Fm.SetPatch(6, Fm.Sine);
        Fm.NoteOn(6, 69, 32);
        Wait(60);
        Fm.NoteOff(6);
        Wait(60);

        Fm.AllOff();

        // What Fm refuses. (Nothing is heard: these are rejected before the chip is written to.)
        byte[] short25 = new byte[25];
        Check("a channel that doesn't exist", !Fm.SetPatch(8, Fm.Sine) && !Fm.NoteOn(8, 69) && !Fm.NoteOn(-1, 69));
        Check("an instrument of the wrong size", !Fm.SetPatch(0, short25) && !Fm.SetPatch(0, new byte[27]));
        Check("notes the chip can't play", !Fm.NoteOn(0, 12) && !Fm.NoteOn(0, 109) && !Fm.NoteOn(0, -5));
        Check("a fine tuning out of range", !Fm.NoteOn(0, 69, 64) && !Fm.NoteOn(0, 69, -1));
        Check("the lowest and the highest note", Fm.NoteOn(7, 13) && Fm.NoteOn(7, 108));
        Fm.AllOff();
        Check("the same instrument is accepted again", Fm.SetPatch(0, Fm.Sine) && Fm.SetPatch(0, Fm.Organ));
        Fm.AllOff();

        CX16.Clear(CX16.Blue);
        if (s_failures == 0)
            Out.Line("All " + s_checks.ToString() + " checks passed.");
        else
            Out.Line(s_failures.ToString() + " of " + s_checks.ToString() + " checks FAILED, the first: " + s_firstFailure);
        return s_failures;
    }
}

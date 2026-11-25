// The Commander X16's FM synthesizer, the YM2151: 8 channels, each of 4 operators
// that modulate one another (an algorithm says how) to make a timbre. Only for
// programs built with ./build.sh --cx16; the function is in cx16/runtime.c, and a
// program for another target that calls it won't link.
//
//   Fm.Reset();
//   Fm.SetPatch(0, Fm.Organ);                   // an instrument on channel 0
//   Fm.NoteOn(0, 69);                           // a note: MIDI 69 is the A of 440 Hz
//   ...
//   Fm.NoteOff(0);
//
// The chip's registers can't be read, so the library keeps what it must know of
// them (each channel's algorithm, the operators' levels, a volume): don't mix
// calls of Fm with Write on the same registers. It also isn't the ROM's audio
// bank (ym_loadpatch with its 162 presets, ym_playnote): those keep a copy of the
// chip's registers of their own, and the two shouldn't drive one chip together.
//
// An instrument is 26 bytes, in the order of the chip's registers (SetPatch has the
// layout). Fm.Sine and Fm.Organ are two; the sound of anything else is a matter of
// the YM2151's manual: the operators' ratios (MUL, DT1), levels (TL: 0 loudest,
// 127 silent), and envelopes (AR, D1R, D2R, D1L, RR).
//
// The sound can't be heard in a test that runs without a screen, so cx16/test-audio.sh
// records it in the emulator and measures the pitch.

using System;
using System.Runtime.InteropServices;

public static class Fm
{
    public const int Channels = 8;

    [DllImport("libc")] static safe extern void cx16_ym_write(int register, int value);

    // What the library must remember: the chip's registers can't be read.
    static readonly byte[] s_control = new byte[Channels];       // RL, FB and CON of each channel ($20)
    static readonly byte[] s_level = new byte[Channels * 4];     // each operator's TL as the instrument set it
    static readonly byte[] s_volume = new byte[Channels];        // 0-127, 127 the instrument's own level
    static bool s_ready;

    // The instruments. Operators in the chip's order: M1, M2, C1, C2 (the key code goes to all four).
    static ReadOnlySpan<byte> SinePatch => new byte[]
    {
        0xC7, 0x00,                       // both speakers, no feedback, algorithm 7 (all four operators sound); no vibrato
        0x01, 0x01, 0x01, 0x01,           // DT1 and MUL (the ratio 1) for each
        0x7F, 0x7F, 0x7F, 0x00,           // TL: only the last operator is heard, the others silent
        0x1F, 0x1F, 0x1F, 0x1F,           // KS and AR: the attack at once
        0x00, 0x00, 0x00, 0x00,           // D1R: no decay
        0x00, 0x00, 0x00, 0x00,           // D2R: none
        0x0F, 0x0F, 0x0F, 0x0F,           // D1L and RR: the quickest release
    };

    static ReadOnlySpan<byte> OrganPatch => new byte[]
    {
        0xC7, 0x00,
        0x01, 0x02, 0x04, 0x08,           // the ratios 1, 2, 4 and 8: the first four partials an octave apart
        0x00, 0x18, 0x24, 0x34,           // TL: each a little quieter than the one before
        0x1F, 0x1F, 0x1F, 0x1F,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x0F, 0x0F, 0x0F, 0x0F,
    };

    /// <summary>A pure tone (one operator): the instrument to hear a pitch with.</summary>
    public static ReadOnlySpan<byte> Sine => SinePatch;

    /// <summary>A steady organ-like tone: four operators an octave apart.</summary>
    public static ReadOnlySpan<byte> Organ => OrganPatch;

    /// <summary>
    /// Writes a value to one of the chip's registers ($00-$FF), when it is ready for it. For what the
    /// library has no call for (the LFO, the noise, the timers); don't use it for the registers it keeps
    /// copies of: $20-$27 (RL, FB, CON), $60-$7F (TL).
    /// </summary>
    public static void Write(int register, int value) => cx16_ym_write(register & 255, value & 255);

    /// <summary>Silences all 8 channels and sets the chip to a known state: no LFO, no noise, every channel with the Sine instrument at full volume. Call it first.</summary>
    public static void Reset()
    {
        for (int channel = 0; channel < Channels; channel++)
            cx16_ym_write(0x08, channel);                     // key off
        cx16_ym_write(0x0F, 0x00);                            // no noise
        cx16_ym_write(0x01, 0x00);                            // LFO running, but nothing uses it
        cx16_ym_write(0x19, 0x00);                            // no amplitude modulation
        cx16_ym_write(0x19, 0x80);                            // and no pitch modulation
        s_ready = true;
        for (int channel = 0; channel < Channels; channel++)
            SetPatch(channel, SinePatch);
    }

    /// <summary>
    /// Gives a channel (0-7) an instrument of 26 bytes, in the order of the chip's registers: byte 0 is
    /// RL (bits 7-6: right, left), FB (5-3), CON (2-0, the algorithm); 1 is PMS and AMS; then six groups of four,
    /// one byte for each operator in the order M1, M2, C1, C2: DT1 and MUL; TL; KS and AR; AMS-EN and D1R;
    /// DT2 and D2R; D1L and RR. Returns false, and changes nothing, for another length or channel. A note
    /// that is sounding is released first.
    /// </summary>
    public static bool SetPatch(int channel, ReadOnlySpan<byte> patch)
    {
        if ((uint)channel >= Channels || patch.Length != 26)
            return false;
        if (!s_ready)
            Reset();
        cx16_ym_write(0x08, channel);                         // key off
        s_control[channel] = patch[0];
        cx16_ym_write(0x20 + channel, patch[0]);
        cx16_ym_write(0x38 + channel, patch[1]);
        for (int op = 0; op < 4; op++)
        {
            int slot = op * 8 + channel;
            s_level[channel * 4 + op] = patch[6 + op];
            cx16_ym_write(0x40 + slot, patch[2 + op]);
            cx16_ym_write(0x80 + slot, patch[10 + op]);
            cx16_ym_write(0xA0 + slot, patch[14 + op]);
            cx16_ym_write(0xC0 + slot, patch[18 + op]);
            cx16_ym_write(0xE0 + slot, patch[22 + op]);
        }
        s_volume[channel] = 127;
        WriteLevels(channel);
        return true;
    }

    // The operators that make the sound, by the algorithm (CON): the others only modulate. Bit 0 is M1,
    // bit 1 M2, bit 2 C1, bit 3 C2.
    static int Carriers(int connection)
        => connection < 4 ? 8 : connection == 4 ? 12 : connection < 7 ? 14 : 15;

    // Writes each operator's level: the instrument's own, and for the ones that sound, quieter by the volume.
    static void WriteLevels(int channel)
    {
        int carriers = Carriers(s_control[channel] & 7);
        for (int op = 0; op < 4; op++)
        {
            int level = s_level[channel * 4 + op] & 0x7F;
            if ((carriers & (1 << op)) != 0)
                level = Math.Min(127, level + (127 - s_volume[channel]));
            cx16_ym_write(0x60 + op * 8 + channel, level);
        }
    }

    /// <summary>The loudness of a channel, 0 (silent) to 127 (the instrument's own level). It is taken off the operators that sound, so the instrument's balance stays.</summary>
    public static void SetVolume(int channel, int volume)
    {
        if ((uint)channel >= Channels || !s_ready)
            return;
        s_volume[channel] = (byte)(volume < 0 ? 0 : volume > 127 ? 127 : volume);
        WriteLevels(channel);
    }

    /// <summary>Which speakers a channel is heard in.</summary>
    public static void SetPan(int channel, bool left, bool right)
    {
        if ((uint)channel >= Channels || !s_ready)
            return;
        s_control[channel] = (byte)((s_control[channel] & 0x3F) | (right ? 0x80 : 0) | (left ? 0x40 : 0));
        cx16_ym_write(0x20 + channel, s_control[channel]);
    }

    // The chip's key code for a MIDI note: the octave in bits 6-4, then a note number that skips 3, 7, 11 and
    // 15 (the chip counts C sharp, D, D sharp, E, F, F sharp, G, G sharp, A, A sharp, B, C of the next octave).
    // MIDI 69 is 440 Hz, which is $4A. The chip is tuned for a 3.58 MHz clock, which the X16 has.
    static int KeyCode(int note, out bool ok)
    {
        int above = note - 61;                  // semitones above C sharp in octave 4, which is $40
        int octave = 4 + (above >= 0 ? above / 12 : -((11 - above) / 12));
        int within = above - (octave - 4) * 12;
        ok = octave >= 0 && octave <= 7;
        ReadOnlySpan<byte> codes = new byte[] { 0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14 };
        return (octave << 4) | codes[within];
    }

    /// <summary>
    /// Starts a note on a channel: a MIDI note number (69 is the A of 440 Hz; the chip reaches 13 to 108,
    /// C sharp 0 to C 8), and a fine tuning of 0 to 63 sixty-fourths of a semitone above it. If the channel was
    /// sounding it starts again. Returns false for a channel or note the chip can't play.
    /// </summary>
    public static bool NoteOn(int channel, int note, int fine = 0)
    {
        if ((uint)channel >= Channels || (uint)fine > 63)
            return false;
        int code = KeyCode(note, out bool ok);
        if (!ok)
            return false;
        if (!s_ready)
            Reset();
        cx16_ym_write(0x08, channel);                         // off, so that it starts again
        cx16_ym_write(0x28 + channel, code);
        cx16_ym_write(0x30 + channel, fine << 2);
        cx16_ym_write(0x08, 0x78 | channel);                  // on, all four operators
        return true;
    }

    /// <summary>Releases the note on a channel: it dies away as its instrument's release says.</summary>
    public static void NoteOff(int channel)
    {
        if ((uint)channel < Channels)
            cx16_ym_write(0x08, channel);
    }

    /// <summary>Releases every channel.</summary>
    public static void AllOff()
    {
        for (int channel = 0; channel < Channels; channel++)
            cx16_ym_write(0x08, channel);
    }
}

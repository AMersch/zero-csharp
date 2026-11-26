#!/usr/bin/env python3
"""Measures a recording of cx16's SoundDemo (x16emu -wav): finds each sound in it by its loudness and checks the
pitch, the partials, the speaker and the volume of each against what the sample plays. Usage: audio-check.py FILE.wav [pcm]  (pcm: the recording is PcmDemo's)

The emulator doesn't finish the file's header when it is stopped (the sizes stay 0), so the samples are read from
after the "data" chunk to the end of the file, 16 bits, as many channels as the format says."""
import struct
import sys

import numpy as np


def read_wav(path):
    d = open(path, "rb").read()
    at = d.find(b"fmt ")
    tag, channels, rate, _, _, bits = struct.unpack("<HHIIHH", d[at + 8:at + 24])
    start = d.find(b"data") + 8
    samples = np.frombuffer(d[start:start + (len(d) - start) // 2 * 2], dtype="<i2").astype(float)
    return samples[:len(samples) // channels * channels].reshape(-1, channels), rate


def segments(x, rate):
    """(start, end) in samples of each stretch of sound: blocks of 10 ms louder than 3% of the loudest, gaps under 150 ms bridged."""
    block = rate // 100
    n = len(x) // block
    loud = np.abs(x).max(axis=1)[:n * block].reshape(n, block).max(axis=1)
    active = loud > 0.03 * loud.max()
    found, i = [], 0
    while i < n:
        if not active[i]:
            i += 1
            continue
        j = i
        gap = 0
        while j < n and gap < 15:
            gap = 0 if active[j] else gap + 1
            j += 1
        end = j - gap
        if (end - i) * 10 >= 300:
            found.append((i * block, end * block))
        i = j
    return found


def spectrum(x, rate, start, end):
    """The middle 70% of a sound, windowed, zero-padded: (frequencies, magnitudes)."""
    length = end - start
    seg = x[start + length * 15 // 100:end - length * 15 // 100]
    win = seg * np.hanning(len(seg))
    padded = 1 << (int(np.ceil(np.log2(len(win)))) + 2)
    mag = np.abs(np.fft.rfft(win, padded))
    return np.fft.rfftfreq(padded, 1 / rate), mag


def peaks(freqs, mag, count=4, floor=0.03):
    """The strongest local maxima, as (frequency, magnitude), loudest first, interpolated between bins."""
    found = []
    for i in range(2, len(mag) - 2):
        if mag[i] > mag[i - 1] and mag[i] >= mag[i + 1] and mag[i] > floor * mag.max():
            a, b, c = np.log(mag[i - 1] + 1e-9), np.log(mag[i] + 1e-9), np.log(mag[i + 1] + 1e-9)
            shift = 0.5 * (a - c) / (a - 2 * b + c) if (a - 2 * b + c) != 0 else 0
            found.append((freqs[i] + shift * (freqs[1] - freqs[0]), mag[i]))
    found.sort(key=lambda p: -p[1])
    kept = []
    for f, m in found:                                   # leave out the side lobes of one peak
        if all(abs(f - g) > 8 for g, _ in kept):
            kept.append((f, m))
    return kept[:count]


def rms(x, start, end):
    length = end - start
    seg = x[start + length * 15 // 100:end - length * 15 // 100]
    return float(np.sqrt(np.mean(seg ** 2)))


def pcm(samples, rate, mono, found):
    """samples/PcmDemo.cs: six sounds."""
    failures = 0

    def check(name, ok, detail):
        nonlocal failures
        print("%s %s: %s" % ("ok  " if ok else "FAIL", name, detail))
        if not ok:
            failures += 1

    if len(found) != 6:
        check("the sounds", False, "expected 6, found %d (%s)" % (len(found), ", ".join("%.2f-%.2f s" % (a / rate, b / rate) for a, b in found)))
        return 1
    top = lambda s: peaks(*spectrum(mono, rate, *found[s]))
    near = lambda f, want, tol: abs(f - want) <= tol
    length = lambda s: (found[s][1] - found[s][0]) / rate
    # The next partial below half the sample rate (above it are the images of the sound at the chip's 24 kHz).
    other = lambda p: max([m / p[0][1] for f, m in p[1:] if f < 12000], default=0)
    chip = lambda step: step * 48828.125 / 128          # the rates the chip really has

    p = top(0)
    third = [m / p[0][1] for f, m in p if near(f, 3 * chip(21) / 20, 6)]
    check("1 square of 8-bit samples, %.1f Hz" % (chip(21) / 20), near(p[0][0], chip(21) / 20, 1.5) and third and 0.25 < third[0] < 0.4,
          "%.2f Hz, partial at three times that %.2f of it (a square: 0.33)" % (p[0][0], third[0] if third else 0))
    check("1 lasts 6000 samples", near(length(0), 6000 / chip(21), 0.03), "%.3f s" % length(0))
    p = top(1)
    check("2 sine of 16-bit samples in a loop, %.1f Hz" % (chip(64) / 32), near(p[0][0], chip(64) / 32, 1.5) and other(p) < 0.02,
          "%.2f Hz, next partial %.3f of it" % (p[0][0], other(p)))
    a, b = found[2]
    left, right = rms(samples[:, 0], a, b), rms(samples[:, 1], a, b)
    check("3 left speaker only", left > 20 * max(right, 1), "left %.0f, right %.0f" % (left, right))
    p = top(3)
    check("4 sine from the banks, %.1f Hz" % (chip(64) / 64), near(p[0][0], chip(64) / 64, 1.0) and other(p) < 0.02,
          "%.2f Hz, next partial %.3f of it" % (p[0][0], other(p)))
    check("4 lasts 40000 samples", near(length(3), 40000 / chip(64), 0.03), "%.3f s" % length(3))
    loud, quiet = rms(mono, *found[4]), rms(mono, *found[5])
    check("6 volume 7 is quieter than 15", 0.02 * loud < quiet < 0.4 * loud, "%.3f of the full volume" % (quiet / loud))
    print("%d of 7 checks failed" % failures if failures else "all 7 checks passed")
    return 1 if failures else 0


def main():
    samples, rate = read_wav(sys.argv[1])
    mono = samples.mean(axis=1) if samples.shape[1] > 1 else samples[:, 0]
    found = segments(samples, rate)
    print("%d sounds found in %.1f s of sound (%d Hz)" % (len(found), len(samples) / rate, rate))
    if sys.argv[2:] == ["pcm"]:
        return pcm(samples, rate, mono, found)
    failures = 0

    def check(name, ok, detail):
        nonlocal failures
        print("%s %s: %s" % ("ok  " if ok else "FAIL", name, detail))
        if not ok:
            failures += 1

    if len(found) != 8:
        check("the sounds", False, "expected 8, found %d (%s)" % (len(found), ", ".join("%.2f-%.2f s" % (a / rate, b / rate) for a, b in found)))
        return 1
    top = lambda s: peaks(*spectrum(mono, rate, *found[s]))
    near = lambda f, want, tol: abs(f - want) <= tol

    p = top(0)
    check("1 PSG triangle, 440 Hz", near(p[0][0], 440, 1.5), "%.2f Hz" % p[0][0])
    p = top(1)
    check("2 FM sine, A 440 Hz", near(p[0][0], 440, 1.5), "%.2f Hz" % p[0][0])
    p = top(2)
    second = [m / p[0][1] for f, m in p if near(f, 440, 3)]
    check("3 FM organ, 220 Hz, with its octave", near(p[0][0], 220, 1.0) and second and 0.08 < second[0] < 0.2,
          "%.2f Hz, partial at 440 Hz %.2f of it (expected about 0.13)" % (p[0][0], second[0] if second else 0))
    p = top(3)
    freqs = sorted(f for f, _ in p[:2])
    check("4 a fifth: 440 and 659.26 Hz", len(p) >= 2 and near(freqs[0], 440, 1.5) and near(freqs[1], 659.26, 2.0) and p[1][1] > 0.5 * p[0][1],
          ", ".join("%.2f Hz" % f for f in freqs))
    a, b = found[4]
    left, right = rms(samples[:, 0], a, b), rms(samples[:, 1], a, b)
    check("5 left speaker only", left > 20 * max(right, 1), "left %.0f, right %.0f" % (left, right))
    loud, quiet = rms(mono, *found[5]), rms(mono, *found[6])
    ratio = quiet / loud
    expected = 10 ** (-27 * 0.75 / 20)
    check("6 volume 100 of 127: 27 steps of 0.75 dB down", 0.8 * expected < ratio < 1.25 * expected,
          "%.3f of the full volume (expected %.3f)" % (ratio, expected))
    p = top(7)
    want = 440 * 2 ** (0.5 / 12)
    check("7 half a semitone up: %.2f Hz" % want, near(p[0][0], want, 1.5), "%.2f Hz" % p[0][0])
    print("%d of 8 checks failed" % failures if failures else "all 8 checks passed")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())

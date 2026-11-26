// AVX and AVX2 operations, each checked lane by lane against scalar code.
// Without AVX2 the operations can't run and the checks pass without them.
// Build: ./build.sh Avx2Ops

using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Avx2OpsSample;

public static class Program
{
    const int Rounds = 64;

    static int s_failures;
    static int s_sectionChecks;
    static int s_sectionFailures;
    static uint s_seed = 2463534242;

    static void Section(string section)
    {
        Console.WriteLine($"{(s_sectionFailures == 0 ? "PASS" : "FAIL")} {section} ({s_sectionChecks} checks)");
        s_sectionChecks = 0;
        s_sectionFailures = 0;
    }

    static void Check(string name, bool ok)
    {
        s_sectionChecks++;
        if (!ok)
        {
            s_failures++;
            s_sectionFailures++;
            Console.WriteLine($"  FAIL: {name}");
        }
    }

    static uint Next()
    {
        uint x = s_seed;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        s_seed = x;
        return x;
    }

    static readonly int[] s_edges = { 0, -1, 1, 255, 256, -256, 32767, -32768, 65535, 65536, int.MaxValue, int.MinValue };

    static int NextLane() => (Next() & 7) == 0 ? s_edges[(int)(Next() % (uint)s_edges.Length)] : (int)Next();

    static void Fill(byte[] a) { for (int i = 0; i < a.Length; i++) a[i] = (byte)NextLane(); }
    static void Fill(short[] a) { for (int i = 0; i < a.Length; i++) a[i] = (short)NextLane(); }
    static void Fill(ushort[] a) { for (int i = 0; i < a.Length; i++) a[i] = (ushort)NextLane(); }
    static void Fill(int[] a) { for (int i = 0; i < a.Length; i++) a[i] = NextLane(); }
    static void Fill(uint[] a) { for (int i = 0; i < a.Length; i++) a[i] = (uint)NextLane(); }
    static void Fill(long[] a) { for (int i = 0; i < a.Length; i++) a[i] = ((long)NextLane() << 32) | (uint)NextLane(); }

    static bool Same(byte[] a, byte[] b) { for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    static bool Same(short[] a, short[] b) { for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    static bool Same(ushort[] a, ushort[] b) { for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    static bool Same(int[] a, int[] b) { for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    static bool Same(uint[] a, uint[] b) { for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    static bool Same(long[] a, long[] b) { for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }

    static bool Same(float[] a, float[] b)
    {
        for (int i = 0; i < a.Length; i++)
            if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i]))
                return false;
        return true;
    }

    static byte SaturateToByte(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
    static ushort SaturateToUInt16(int v) => (ushort)(v < 0 ? 0 : v > 65535 ? 65535 : v);

    static void Permute2x128(int[] left, int[] right, int control, int[] result)
    {
        for (int half = 0; half < 2; half++)
        {
            int select = control >> (4 * half);
            for (int i = 0; i < 4; i++)
                result[4 * half + i] = (select & 8) != 0 ? 0 : ((select & 2) == 0 ? left : right)[4 * (select & 1) + i];
        }
    }

    static void Permute2x128(float[] left, float[] right, int control, float[] result)
    {
        for (int half = 0; half < 2; half++)
        {
            int select = control >> (4 * half);
            for (int i = 0; i < 4; i++)
                result[4 * half + i] = (select & 8) != 0 ? 0 : ((select & 2) == 0 ? left : right)[4 * (select & 1) + i];
        }
    }

    static void LoadsAndStores()
    {
        byte[] bytes = new byte[32], bytesOut = new byte[32];
        short[] shorts = new short[16], shortsOut = new short[16];
        ushort[] ushorts = new ushort[16], ushortsOut = new ushort[16];
        uint[] uints = new uint[8], uintsOut = new uint[8];
        bool ok = true;
        for (int round = 0; round < Rounds && Avx.IsSupported; round++)
        {
            Fill(bytes);
            Fill(shorts);
            Fill(ushorts);
            Fill(uints);
            unsafe
            {
                fixed (byte* p = bytes) fixed (byte* q = bytesOut) Avx.Store(q, Avx.LoadVector256(p));
                fixed (short* p = shorts) fixed (short* q = shortsOut) Avx.Store(q, Avx.LoadVector256(p));
                fixed (ushort* p = ushorts) fixed (ushort* q = ushortsOut) Avx.Store(q, Avx.LoadVector256(p));
                fixed (uint* p = uints) fixed (uint* q = uintsOut) Avx.Store(q, Avx.LoadVector256(p));
            }
            ok &= Same(bytes, bytesOut) && Same(shorts, shortsOut) && Same(ushorts, ushortsOut) && Same(uints, uintsOut);
        }
        Check("LoadVector256 and Store of byte, short, ushort, uint", ok);
    }

    static void Conversions()
    {
        int[] ints = new int[8], intsOut = new int[8], intsWant = new int[8];
        float[] floats = new float[8], floatsOut = new float[8], floatsWant = new float[8];
        bool toSingle = true, truncated = true;
        for (int round = 0; round < Rounds && Avx.IsSupported; round++)
        {
            Fill(ints);
            for (int i = 0; i < 8; i++)
            {
                floatsWant[i] = ints[i];
                floats[i] = (int)(Next() % 2000001) - 1000000 + (Next() & 3) * 0.25f;
                intsWant[i] = (int)floats[i];
            }
            unsafe
            {
                fixed (int* p = ints) fixed (float* q = floatsOut)
                    Avx.Store(q, Avx.ConvertToVector256Single(Avx.LoadVector256(p)));
                fixed (float* p = floats) fixed (int* q = intsOut)
                    Avx.Store(q, Avx.ConvertToVector256Int32WithTruncation(Avx.LoadVector256(p)));
            }
            toSingle &= Same(floatsOut, floatsWant);
            truncated &= Same(intsOut, intsWant);
        }
        Check("ConvertToVector256Single", toSingle);
        Check("ConvertToVector256Int32WithTruncation", truncated);
    }

    static void Arithmetic()
    {
        short[] a = new short[16], b = new short[16], got = new short[16], want = new short[16];
        ushort[] ua = new ushort[16], ub = new ushort[16], ugot = new ushort[16], uwant = new ushort[16];
        int[] ia = new int[8], ib = new int[8], igot = new int[8], iwant = new int[8];
        bool[] ok = { true, true, true, true, true, true, true, true, true, true, true, true, true };
        for (int round = 0; round < Rounds && Avx2.IsSupported; round++)
        {
            Fill(a); Fill(b); Fill(ua); Fill(ub); Fill(ia); Fill(ib);
            unsafe
            {
                fixed (short* pa = a) fixed (short* pb = b) fixed (short* pg = got)
                fixed (ushort* qa = ua) fixed (ushort* qb = ub) fixed (ushort* qg = ugot)
                fixed (int* ra = ia) fixed (int* rb = ib) fixed (int* rg = igot)
                {
                    Vector256<short> va = Avx.LoadVector256(pa), vb = Avx.LoadVector256(pb);
                    Vector256<ushort> wa = Avx.LoadVector256(qa), wb = Avx.LoadVector256(qb);
                    Vector256<int> xa = Avx.LoadVector256(ra), xb = Avx.LoadVector256(rb);

                    Avx.Store(pg, Avx2.Add(va, vb));
                    for (int i = 0; i < 16; i++) want[i] = (short)(a[i] + b[i]);
                    ok[0] &= Same(got, want);
                    Avx.Store(pg, Avx2.Subtract(va, vb));
                    for (int i = 0; i < 16; i++) want[i] = (short)(a[i] - b[i]);
                    ok[1] &= Same(got, want);
                    Avx.Store(pg, Avx2.MultiplyLow(va, vb));
                    for (int i = 0; i < 16; i++) want[i] = (short)(a[i] * b[i]);
                    ok[2] &= Same(got, want);

                    Avx.Store(qg, Avx2.Add(wa, wb));
                    for (int i = 0; i < 16; i++) uwant[i] = (ushort)(ua[i] + ub[i]);
                    ok[3] &= Same(ugot, uwant);
                    Avx.Store(qg, Avx2.Subtract(wa, wb));
                    for (int i = 0; i < 16; i++) uwant[i] = (ushort)(ua[i] - ub[i]);
                    ok[4] &= Same(ugot, uwant);
                    Avx.Store(qg, Avx2.MultiplyLow(wa, wb));
                    for (int i = 0; i < 16; i++) uwant[i] = (ushort)(ua[i] * ub[i]);
                    ok[5] &= Same(ugot, uwant);
                    Avx.Store(qg, Avx2.And(wa, wb));
                    for (int i = 0; i < 16; i++) uwant[i] = (ushort)(ua[i] & ub[i]);
                    ok[6] &= Same(ugot, uwant);
                    Avx.Store(qg, Avx2.Xor(wa, wb));
                    for (int i = 0; i < 16; i++) uwant[i] = (ushort)(ua[i] ^ ub[i]);
                    ok[7] &= Same(ugot, uwant);

                    Avx.Store(rg, Avx2.Subtract(xa, xb));
                    for (int i = 0; i < 8; i++) iwant[i] = unchecked(ia[i] - ib[i]);
                    ok[8] &= Same(igot, iwant);
                    Avx.Store(rg, Avx2.MultiplyLow(xa, xb));
                    for (int i = 0; i < 8; i++) iwant[i] = unchecked(ia[i] * ib[i]);
                    ok[9] &= Same(igot, iwant);
                    Avx.Store(rg, Avx2.CompareGreaterThan(xa, xb));
                    for (int i = 0; i < 8; i++) iwant[i] = ia[i] > ib[i] ? -1 : 0;
                    ok[10] &= Same(igot, iwant);
                    Avx.Store(rg, Avx2.And(xa, xb));
                    for (int i = 0; i < 8; i++) iwant[i] = ia[i] & ib[i];
                    ok[11] &= Same(igot, iwant);
                    Avx.Store(rg, Avx2.Xor(xa, xb));
                    for (int i = 0; i < 8; i++) iwant[i] = ia[i] ^ ib[i];
                    ok[12] &= Same(igot, iwant);
                }
            }
        }
        Check("Add(short), wrapping", ok[0]);
        Check("Subtract(short), wrapping", ok[1]);
        Check("MultiplyLow(short)", ok[2]);
        Check("Add(ushort), wrapping", ok[3]);
        Check("Subtract(ushort), wrapping", ok[4]);
        Check("MultiplyLow(ushort)", ok[5]);
        Check("And(ushort)", ok[6]);
        Check("Xor(ushort)", ok[7]);
        Check("Subtract(int), wrapping", ok[8]);
        Check("MultiplyLow(int)", ok[9]);
        Check("CompareGreaterThan(int), signed", ok[10]);
        Check("And(int)", ok[11]);
        Check("Xor(int)", ok[12]);
    }

    static void Widening()
    {
        byte[] bytes = new byte[16];
        ushort[] ushorts = new ushort[8];
        short[] shorts = new short[16], shortsWant = new short[16];
        int[] ints = new int[8], intsWant = new int[8];
        bool toInt16 = true, bytesToInt32 = true, ushortsToInt32 = true;
        for (int round = 0; round < Rounds && Avx2.IsSupported; round++)
        {
            Fill(bytes);
            Fill(ushorts);
            unsafe
            {
                fixed (byte* p = bytes) fixed (ushort* u = ushorts) fixed (short* s = shorts) fixed (int* n = ints)
                {
                    Avx.Store(s, Avx2.ConvertToVector256Int16(p));
                    for (int i = 0; i < 16; i++) shortsWant[i] = bytes[i];
                    toInt16 &= Same(shorts, shortsWant);
                    Avx.Store(n, Avx2.ConvertToVector256Int32(p));
                    for (int i = 0; i < 8; i++) intsWant[i] = bytes[i];
                    bytesToInt32 &= Same(ints, intsWant);
                    Avx.Store(n, Avx2.ConvertToVector256Int32(u));
                    for (int i = 0; i < 8; i++) intsWant[i] = ushorts[i];
                    ushortsToInt32 &= Same(ints, intsWant);
                }
            }
        }
        Check("ConvertToVector256Int16(byte*), zero-extended", toInt16);
        Check("ConvertToVector256Int32(byte*), zero-extended", bytesToInt32);
        Check("ConvertToVector256Int32(ushort*), zero-extended", ushortsToInt32);
    }

    static void PacksAndUnpacks()
    {
        short[] a = new short[16], b = new short[16], got = new short[16], want = new short[16];
        int[] ia = new int[8], ib = new int[8];
        byte[] bytes = new byte[32], bytesWant = new byte[32];
        ushort[] ushorts = new ushort[16], ushortsWant = new ushort[16];
        bool packBytes = true, packUShorts = true, low = true, high = true;
        for (int round = 0; round < Rounds && Avx2.IsSupported; round++)
        {
            Fill(a); Fill(b); Fill(ia); Fill(ib);
            unsafe
            {
                fixed (short* pa = a) fixed (short* pb = b) fixed (short* pg = got)
                fixed (int* ra = ia) fixed (int* rb = ib) fixed (byte* pbytes = bytes) fixed (ushort* pushorts = ushorts)
                {
                    Vector256<short> va = Avx.LoadVector256(pa), vb = Avx.LoadVector256(pb);
                    Vector256<int> xa = Avx.LoadVector256(ra), xb = Avx.LoadVector256(rb);
                    Avx.Store(pbytes, Avx2.PackUnsignedSaturate(va, vb));
                    Avx.Store(pushorts, Avx2.PackUnsignedSaturate(xa, xb));
                    Avx.Store(pg, Avx2.UnpackLow(va, vb));
                    for (int half = 0; half < 2; half++)
                        for (int i = 0; i < 4; i++)
                        {
                            want[8 * half + 2 * i] = a[8 * half + i];
                            want[8 * half + 2 * i + 1] = b[8 * half + i];
                        }
                    low &= Same(got, want);
                    Avx.Store(pg, Avx2.UnpackHigh(va, vb));
                    for (int half = 0; half < 2; half++)
                        for (int i = 0; i < 4; i++)
                        {
                            want[8 * half + 2 * i] = a[8 * half + 4 + i];
                            want[8 * half + 2 * i + 1] = b[8 * half + 4 + i];
                        }
                    high &= Same(got, want);
                }
            }
            for (int half = 0; half < 2; half++)
                for (int i = 0; i < 8; i++)
                {
                    bytesWant[16 * half + i] = SaturateToByte(a[8 * half + i]);
                    bytesWant[16 * half + 8 + i] = SaturateToByte(b[8 * half + i]);
                }
            for (int half = 0; half < 2; half++)
                for (int i = 0; i < 4; i++)
                {
                    ushortsWant[8 * half + i] = SaturateToUInt16(ia[4 * half + i]);
                    ushortsWant[8 * half + 4 + i] = SaturateToUInt16(ib[4 * half + i]);
                }
            packBytes &= Same(bytes, bytesWant);
            packUShorts &= Same(ushorts, ushortsWant);
        }
        Check("PackUnsignedSaturate(short) to byte", packBytes);
        Check("PackUnsignedSaturate(int) to ushort", packUShorts);
        Check("UnpackLow(short)", low);
        Check("UnpackHigh(short)", high);
    }

    static void Permutes()
    {
        int[] a = new int[8], b = new int[8], control = new int[8], got = new int[8], want = new int[8];
        long[] l = new long[4], lgot = new long[4], lwant = new long[4];
        float[] f = new float[8], g = new float[8], fgot = new float[8], fwant = new float[8];
        ushort[] broadcast = new ushort[16], broadcastWant = new ushort[16];
        bool var8x32 = true, permute4x64 = true, int2x128 = true, float2x128 = true, broadcasts = true;
        for (int round = 0; round < Rounds && Avx2.IsSupported; round++)
        {
            Fill(a); Fill(b); Fill(control); Fill(l);
            for (int i = 0; i < 8; i++)
            {
                f[i] = (int)Next() * 0.5f;
                g[i] = (int)Next() * 0.25f;
            }
            ushort scalar = (ushort)Next();
            unsafe
            {
                fixed (int* pa = a) fixed (int* pb = b) fixed (int* pc = control) fixed (int* pg = got)
                fixed (long* pl = l) fixed (long* plg = lgot)
                fixed (float* pf = f) fixed (float* pg2 = g) fixed (float* pfg = fgot)
                fixed (ushort* pbr = broadcast)
                {
                    Vector256<int> va = Avx.LoadVector256(pa), vb = Avx.LoadVector256(pb);
                    Vector256<float> vf = Avx.LoadVector256(pf), vg = Avx.LoadVector256(pg2);

                    Avx.Store(pg, Avx2.PermuteVar8x32(va, Avx.LoadVector256(pc)));
                    for (int i = 0; i < 8; i++) want[i] = a[control[i] & 7];
                    var8x32 &= Same(got, want);

                    Vector256<long> vl = Avx.LoadVector256((int*)pl).AsInt64();
                    Avx.Store((int*)plg, Avx2.Permute4x64(vl, 0xD8).AsInt32());
                    lwant[0] = l[0]; lwant[1] = l[2]; lwant[2] = l[1]; lwant[3] = l[3];
                    permute4x64 &= Same(lgot, lwant);
                    Avx.Store((int*)plg, Avx2.Permute4x64(vl, 0x1B).AsInt32());
                    lwant[0] = l[3]; lwant[1] = l[2]; lwant[2] = l[1]; lwant[3] = l[0];
                    permute4x64 &= Same(lgot, lwant);

                    Avx.Store(pg, Avx2.Permute2x128(va, vb, 0x20));
                    Permute2x128(a, b, 0x20, want);
                    int2x128 &= Same(got, want);
                    Avx.Store(pg, Avx2.Permute2x128(va, vb, 0x31));
                    Permute2x128(a, b, 0x31, want);
                    int2x128 &= Same(got, want);
                    Avx.Store(pg, Avx2.Permute2x128(va, vb, 0x03));
                    Permute2x128(a, b, 0x03, want);
                    int2x128 &= Same(got, want);
                    Avx.Store(pg, Avx2.Permute2x128(va, vb, 0x82));
                    Permute2x128(a, b, 0x82, want);
                    int2x128 &= Same(got, want);

                    Avx.Store(pfg, Avx.Permute2x128(vf, vg, 0x21));
                    Permute2x128(f, g, 0x21, fwant);
                    float2x128 &= Same(fgot, fwant);
                    Avx.Store(pfg, Avx.Permute2x128(vf, vg, 0x30));
                    Permute2x128(f, g, 0x30, fwant);
                    float2x128 &= Same(fgot, fwant);
                    Avx.Store(pfg, Avx.Permute2x128(vf, vg, 0x18));
                    Permute2x128(f, g, 0x18, fwant);
                    float2x128 &= Same(fgot, fwant);

                    Avx.Store(pbr, Avx2.BroadcastScalarToVector256(&scalar));
                    for (int i = 0; i < 16; i++) broadcastWant[i] = scalar;
                    broadcasts &= Same(broadcast, broadcastWant);
                }
            }
        }
        Check("PermuteVar8x32", var8x32);
        Check("Permute4x64", permute4x64);
        Check("Avx2.Permute2x128(int)", int2x128);
        Check("Avx.Permute2x128(float)", float2x128);
        Check("BroadcastScalarToVector256(ushort*)", broadcasts);
    }

    static void VectorHelpers()
    {
        int[] ints = new int[8], intsWant = new int[8];
        uint[] uints = new uint[8];
        long[] longs = new long[4];
        short[] shorts = new short[16];
        ushort[] ushorts = new ushort[16];
        byte[] bytes = new byte[32];
        float[] floats = new float[8], floatsWant = new float[8];
        bool zero = true, one = true, create = true, reinterpret = true, sum = true;
        for (int round = 0; round < Rounds && Avx2.IsSupported; round++)
        {
            Fill(ints);
            int e0 = NextLane(), e7 = NextLane();
            short s = (short)NextLane();
            ushort u = (ushort)NextLane();
            int intSum = 0;
            float floatSum = 0;
            for (int i = 0; i < 8; i++)
            {
                intSum = unchecked(intSum + ints[i]);
                floats[i] = (int)(Next() % 2001) - 1000;
                floatSum += floats[i];
            }
            unsafe
            {
                fixed (int* pi = ints) fixed (int* pw = intsWant) fixed (uint* pu = uints) fixed (long* pl = longs)
                fixed (short* ps = shorts) fixed (ushort* pus = ushorts) fixed (byte* pb = bytes) fixed (float* pf = floats)
                fixed (float* pfw = floatsWant)
                {
                    Vector256<int> v = Avx.LoadVector256(pi);

                    Avx.Store(pw, Vector256<int>.Zero);
                    for (int i = 0; i < 8; i++) zero &= intsWant[i] == 0;
                    Avx.Store(pw, Vector256<int>.One);
                    for (int i = 0; i < 8; i++) one &= intsWant[i] == 1;
                    Avx.Store(pus, Vector256<ushort>.One);
                    for (int i = 0; i < 16; i++) one &= ushorts[i] == 1;
                    Avx.Store(pfw, Vector256<float>.One);
                    for (int i = 0; i < 8; i++) one &= floatsWant[i] == 1f;

                    Avx.Store(ps, Vector256.Create(s));
                    for (int i = 0; i < 16; i++) create &= shorts[i] == s;
                    Avx.Store(pus, Vector256.Create(u));
                    for (int i = 0; i < 16; i++) create &= ushorts[i] == u;
                    Avx.Store(pw, Vector256.Create(e0, 1, 2, 3, 4, 5, 6, e7));
                    create &= intsWant[0] == e0 && intsWant[7] == e7;
                    for (int i = 1; i < 7; i++) create &= intsWant[i] == i;

                    Avx.Store(pb, v.AsByte());
                    Avx.Store(ps, v.AsInt16());
                    Avx.Store(pus, v.AsUInt16());
                    Avx.Store(pu, v.AsUInt32());
                    Avx.Store((int*)pl, v.AsInt64().AsInt32());
                    byte* raw = (byte*)pi;
                    for (int i = 0; i < 32; i++)
                        reinterpret &= bytes[i] == raw[i] && ((byte*)ps)[i] == raw[i] && ((byte*)pus)[i] == raw[i]
                            && ((byte*)pu)[i] == raw[i] && ((byte*)pl)[i] == raw[i];

                    sum &= Vector256.Sum(v) == intSum;
                    sum &= Vector256.Sum(Avx.LoadVector256(pf)) == floatSum;
                }
            }
        }
        Check("Vector256<T>.Zero", zero);
        Check("Vector256<T>.One of int, ushort, float", one);
        Check("Vector256.Create of short, ushort, 8 ints", create);
        Check("AsByte, AsInt16, AsUInt16, AsUInt32, AsInt64", reinterpret);
        Check("Vector256.Sum of int (wrapping) and float", sum);
    }

    public static int Main()
    {
        LoadsAndStores();
        Conversions();
        Section("Avx loads, stores and conversions");
        Arithmetic();
        Section("Avx2 arithmetic");
        Widening();
        Section("Avx2 widening loads");
        PacksAndUnpacks();
        Section("Avx2 packs and unpacks");
        Permutes();
        Section("Avx2 permutes and broadcasts");
        VectorHelpers();
        Section("Vector256 helpers");

        Console.WriteLine();
        if (s_failures == 0)
            Console.WriteLine("All checks passed.");
        else
            Console.WriteLine($"{s_failures} checks FAILED.");
        return s_failures;
    }
}

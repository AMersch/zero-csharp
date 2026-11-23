using System.Collections.Generic;
using System.Numerics;

namespace System
{
    // HashCode.Combine(a, b, c) and Add/ToHashCode, for writing GetHashCode:
    //
    //   public override int GetHashCode() => HashCode.Combine(X, Y, Name);
    //
    // The same algorithm as .NET's (based on xxHash32), with one difference:
    // .NET seeds it with a random number per process, so hash codes change from
    // run to run; zero mode has no source of randomness at startup, so the seed
    // is fixed and the same values always give the same hash code. Don't rely on
    // either: save or send the values, not their hash codes.
    public struct HashCode
    {
        private const uint Seed = 0x9E3779B9;

        private const uint Prime1 = 2654435761U;
        private const uint Prime2 = 2246822519U;
        private const uint Prime3 = 3266489917U;
        private const uint Prime4 = 668265263U;
        private const uint Prime5 = 374761393U;

        private uint _v1, _v2, _v3, _v4;
        private uint _queue1, _queue2, _queue3;
        private uint _length;

        public static int Combine<T1>(T1 value1)
        {
            uint hash = MixEmptyState() + 4;
            hash = QueueRound(hash, Hash(value1));
            return (int)MixFinal(hash);
        }

        public static int Combine<T1, T2>(T1 value1, T2 value2)
        {
            uint hash = MixEmptyState() + 8;
            hash = QueueRound(hash, Hash(value1));
            hash = QueueRound(hash, Hash(value2));
            return (int)MixFinal(hash);
        }

        public static int Combine<T1, T2, T3>(T1 value1, T2 value2, T3 value3)
        {
            uint hash = MixEmptyState() + 12;
            hash = QueueRound(hash, Hash(value1));
            hash = QueueRound(hash, Hash(value2));
            hash = QueueRound(hash, Hash(value3));
            return (int)MixFinal(hash);
        }

        public static int Combine<T1, T2, T3, T4>(T1 value1, T2 value2, T3 value3, T4 value4)
        {
            Initialize(out uint v1, out uint v2, out uint v3, out uint v4);
            v1 = Round(v1, Hash(value1));
            v2 = Round(v2, Hash(value2));
            v3 = Round(v3, Hash(value3));
            v4 = Round(v4, Hash(value4));
            uint hash = MixState(v1, v2, v3, v4) + 16;
            return (int)MixFinal(hash);
        }

        public void Add<T>(T value) => Add(Hash(value));

        public void Add<T>(T value, IEqualityComparer<T> comparer)
            => Add(value == null ? 0u : comparer != null ? (uint)comparer.GetHashCode(value) : Hash(value));

        // The hash code of everything added so far.
        public readonly int ToHashCode()
        {
            uint length = _length;
            uint position = length % 4;
            uint hash = length < 4 ? MixEmptyState() : MixState(_v1, _v2, _v3, _v4);
            hash += length * 4;
            if (position > 0)
            {
                hash = QueueRound(hash, _queue1);
                if (position > 1)
                {
                    hash = QueueRound(hash, _queue2);
                    if (position > 2)
                        hash = QueueRound(hash, _queue3);
                }
            }
            return (int)MixFinal(hash);
        }

        // Values are queued until there are four, then mixed in together.
        private void Add(uint value)
        {
            uint previousLength = _length++;
            switch (previousLength % 4)
            {
                case 0: _queue1 = value; break;
                case 1: _queue2 = value; break;
                case 2: _queue3 = value; break;
                default:
                    if (previousLength == 3)
                        Initialize(out _v1, out _v2, out _v3, out _v4);
                    _v1 = Round(_v1, _queue1);
                    _v2 = Round(_v2, _queue2);
                    _v3 = Round(_v3, _queue3);
                    _v4 = Round(_v4, value);
                    break;
            }
        }

        // A struct's own GetHashCode, called directly; zero for null.
        private static uint Hash<T>(T value) => value == null ? 0u : (uint)value.GetHashCode();

        private static void Initialize(out uint v1, out uint v2, out uint v3, out uint v4)
        {
            v1 = unchecked(Seed + Prime1 + Prime2);
            v2 = unchecked(Seed + Prime2);
            v3 = Seed;
            v4 = unchecked(Seed - Prime1);
        }

        private static uint Round(uint hash, uint input) => BitOperations.RotateLeft(hash + input * Prime2, 13) * Prime1;

        private static uint QueueRound(uint hash, uint queuedValue) => BitOperations.RotateLeft(hash + queuedValue * Prime3, 17) * Prime4;

        private static uint MixState(uint v1, uint v2, uint v3, uint v4)
            => BitOperations.RotateLeft(v1, 1) + BitOperations.RotateLeft(v2, 7) + BitOperations.RotateLeft(v3, 12) + BitOperations.RotateLeft(v4, 18);

        private static uint MixEmptyState() => unchecked(Seed + Prime5);

        private static uint MixFinal(uint hash)
        {
            hash ^= hash >> 15;
            hash *= Prime2;
            hash ^= hash >> 13;
            hash *= Prime3;
            hash ^= hash >> 16;
            return hash;
        }
    }
}

namespace System
{
    // A length of time, in ticks of 100 ns, with .NET's members for making,
    // reading, comparing and adding them, and its constant ("c") text form:
    // [-][d.]hh:mm:ss[.fffffff]. No parsing, custom formats or Multiply/Divide.
    // The From* methods taking a double truncate to a tick, as .NET's do;
    // past the range they throw OverflowException.
    public readonly struct TimeSpan : IEquatable<TimeSpan>, IComparable<TimeSpan>, IComparable
    {
        public const long TicksPerMicrosecond = 10;
        public const long TicksPerMillisecond = 10_000;
        public const long TicksPerSecond = 10_000_000;
        public const long TicksPerMinute = 600_000_000;
        public const long TicksPerHour = 36_000_000_000;
        public const long TicksPerDay = 864_000_000_000;

        public static readonly TimeSpan Zero = new TimeSpan(0);
        public static readonly TimeSpan MaxValue = new TimeSpan(long.MaxValue);
        public static readonly TimeSpan MinValue = new TimeSpan(long.MinValue);

        private readonly long _ticks;

        public TimeSpan(long ticks) => _ticks = ticks;

        public TimeSpan(int hours, int minutes, int seconds)
            : this(0, hours, minutes, seconds, 0) { }

        public TimeSpan(int days, int hours, int minutes, int seconds)
            : this(days, hours, minutes, seconds, 0) { }

        public TimeSpan(int days, int hours, int minutes, int seconds, int milliseconds)
        {
            // Every part fits a long of milliseconds; only the total can overflow ticks.
            long totalMilliseconds = ((long)days * 86_400 + (long)hours * 3600 + (long)minutes * 60 + seconds) * 1000 + milliseconds;
            if (totalMilliseconds > long.MaxValue / TicksPerMillisecond || totalMilliseconds < long.MinValue / TicksPerMillisecond)
                throw new ArgumentOutOfRangeException(null, "TimeSpan overflowed because the duration is too long.");
            _ticks = totalMilliseconds * TicksPerMillisecond;
        }

        public long Ticks => _ticks;

        public int Days => (int)(_ticks / TicksPerDay);
        public int Hours => (int)(_ticks / TicksPerHour % 24);
        public int Minutes => (int)(_ticks / TicksPerMinute % 60);
        public int Seconds => (int)(_ticks / TicksPerSecond % 60);
        public int Milliseconds => (int)(_ticks / TicksPerMillisecond % 1000);

        public double TotalDays => (double)_ticks / TicksPerDay;
        public double TotalHours => (double)_ticks / TicksPerHour;
        public double TotalMinutes => (double)_ticks / TicksPerMinute;
        public double TotalSeconds => (double)_ticks / TicksPerSecond;
        public double TotalMilliseconds
        {
            get
            {
                double milliseconds = (double)_ticks / TicksPerMillisecond;
                // .NET clamps to the range of whole milliseconds a TimeSpan can hold.
                const double Max = (double)(long.MaxValue / TicksPerMillisecond);
                const double Min = (double)(long.MinValue / TicksPerMillisecond);
                return milliseconds > Max ? Max : milliseconds < Min ? Min : milliseconds;
            }
        }

        public static TimeSpan FromTicks(long ticks) => new TimeSpan(ticks);
        public static TimeSpan FromDays(double value) => Interval(value, TicksPerDay);
        public static TimeSpan FromHours(double value) => Interval(value, TicksPerHour);
        public static TimeSpan FromMinutes(double value) => Interval(value, TicksPerMinute);
        public static TimeSpan FromSeconds(double value) => Interval(value, TicksPerSecond);
        public static TimeSpan FromMilliseconds(double value) => Interval(value, TicksPerMillisecond);

        private static TimeSpan Interval(double value, double scale)
        {
            if (value != value)
                throw new ArgumentException("TimeSpan does not accept floating point Not-a-Number values.");
            double ticks = value * scale;
            if (ticks > long.MaxValue || ticks < long.MinValue)
                throw new OverflowException("TimeSpan overflowed because the duration is too long.");
            return ticks >= long.MaxValue ? MaxValue : new TimeSpan((long)ticks);
        }

        public TimeSpan Add(TimeSpan ts) => this + ts;
        public TimeSpan Subtract(TimeSpan ts) => this - ts;
        public TimeSpan Negate() => -this;

        /// <summary>The absolute value.</summary>
        public TimeSpan Duration()
        {
            if (_ticks == long.MinValue)
                throw new OverflowException("The duration cannot be returned for TimeSpan.MinValue because the absolute value of TimeSpan.MinValue exceeds the value of TimeSpan.MaxValue.");
            return new TimeSpan(_ticks < 0 ? -_ticks : _ticks);
        }

        public static TimeSpan operator +(TimeSpan t1, TimeSpan t2)
        {
            long result = t1._ticks + t2._ticks;
            // Overflow: both had the same sign and the result's differs.
            if ((t1._ticks >> 63) == (t2._ticks >> 63) && (t1._ticks >> 63) != (result >> 63))
                throw new OverflowException("TimeSpan overflowed because the duration is too long.");
            return new TimeSpan(result);
        }

        public static TimeSpan operator -(TimeSpan t1, TimeSpan t2)
        {
            long result = t1._ticks - t2._ticks;
            // Overflow: the signs differed and the result's differs from the first's.
            if ((t1._ticks >> 63) != (t2._ticks >> 63) && (t1._ticks >> 63) != (result >> 63))
                throw new OverflowException("TimeSpan overflowed because the duration is too long.");
            return new TimeSpan(result);
        }

        public static TimeSpan operator -(TimeSpan t)
        {
            if (t._ticks == long.MinValue)
                throw new OverflowException("Negating the minimum value of a twos complement number is invalid.");
            return new TimeSpan(-t._ticks);
        }

        public static TimeSpan operator +(TimeSpan t) => t;

        public static bool operator ==(TimeSpan t1, TimeSpan t2) => t1._ticks == t2._ticks;
        public static bool operator !=(TimeSpan t1, TimeSpan t2) => t1._ticks != t2._ticks;
        public static bool operator <(TimeSpan t1, TimeSpan t2) => t1._ticks < t2._ticks;
        public static bool operator >(TimeSpan t1, TimeSpan t2) => t1._ticks > t2._ticks;
        public static bool operator <=(TimeSpan t1, TimeSpan t2) => t1._ticks <= t2._ticks;
        public static bool operator >=(TimeSpan t1, TimeSpan t2) => t1._ticks >= t2._ticks;

        public static int Compare(TimeSpan t1, TimeSpan t2) => t1._ticks.CompareTo(t2._ticks);
        public int CompareTo(TimeSpan value) => _ticks.CompareTo(value._ticks);

        public int CompareTo(object value)
        {
            if (value == null)
                return 1;
            if (value is not TimeSpan other)
                throw new ArgumentException("Object must be of type TimeSpan.");
            return CompareTo(other);
        }

        public static bool Equals(TimeSpan t1, TimeSpan t2) => t1._ticks == t2._ticks;
        public bool Equals(TimeSpan obj) => _ticks == obj._ticks;
        public override bool Equals(object value) => value is TimeSpan other && _ticks == other._ticks;
        public override int GetHashCode() => _ticks.GetHashCode();

        /// <summary>[-][d.]hh:mm:ss[.fffffff], as .NET's "c" format.</summary>
        public override string ToString()
        {
            // Longest: "-10675199.02:48:05.4775808", 26 characters.
            Span<char> text = stackalloc char[26];
            int length = 0;
            ulong ticks = _ticks < 0 ? (ulong)-(_ticks + 1) + 1 : (ulong)_ticks;
            if (_ticks < 0)
                text[length++] = '-';
            ulong days = ticks / TicksPerDay;
            ulong rest = ticks % TicksPerDay;
            if (days != 0)
            {
                length += Digits(text.Slice(length), days, 1);
                text[length++] = '.';
            }
            length += Digits(text.Slice(length), rest / TicksPerHour, 2);
            text[length++] = ':';
            length += Digits(text.Slice(length), rest / TicksPerMinute % 60, 2);
            text[length++] = ':';
            length += Digits(text.Slice(length), rest / TicksPerSecond % 60, 2);
            ulong fraction = rest % TicksPerSecond;
            if (fraction != 0)
            {
                text[length++] = '.';
                length += Digits(text.Slice(length), fraction, 7);
            }
            return string.FromChars(text.Slice(0, length));
        }

        // Writes value in decimal, at least minDigits long; returns how many.
        private static int Digits(Span<char> destination, ulong value, int minDigits)
        {
            int count = 0;
            for (ulong v = value; v != 0 || count < minDigits; v /= 10)
                count++;
            for (int i = count - 1; i >= 0; i--, value /= 10)
                destination[i] = (char)('0' + (int)(value % 10));
            return count;
        }
    }
}

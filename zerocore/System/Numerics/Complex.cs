namespace System.Numerics
{
    // A complex number, as in .NET: two doubles, the same members and, for
    // the ordinary cases, the same results. The functions follow .NET's
    // formulas (Smith's division, the overflow-safe magnitude) except Asin and
    // Acos, which use Kahan's ("Branch Cuts for Complex Elementary Functions",
    // 1987) in place of .NET's longer Hull et al. version: the same branch
    // cuts, accurate to a few units in the last place.
    public readonly struct Complex : IEquatable<Complex>
    {
        private readonly double m_real;
        private readonly double m_imaginary;

        public Complex(double real, double imaginary)
        {
            m_real = real;
            m_imaginary = imaginary;
        }

        public static Complex Zero => new Complex(0, 0);
        public static Complex One => new Complex(1, 0);
        public static Complex ImaginaryOne => new Complex(0, 1);
        public static Complex NaN => new Complex(double.NaN, double.NaN);
        public static Complex Infinity => new Complex(double.PositiveInfinity, double.PositiveInfinity);

        public double Real => m_real;
        public double Imaginary => m_imaginary;
        public double Magnitude => Abs(this);
        public double Phase => Math.Atan2(m_imaginary, m_real);

        public static Complex FromPolarCoordinates(double magnitude, double phase) =>
            new Complex(magnitude * Math.Cos(phase), magnitude * Math.Sin(phase));

        public static Complex Negate(Complex value) => -value;
        public static Complex Add(Complex left, Complex right) => left + right;
        public static Complex Add(Complex left, double right) => left + right;
        public static Complex Add(double left, Complex right) => left + right;
        public static Complex Subtract(Complex left, Complex right) => left - right;
        public static Complex Subtract(Complex left, double right) => left - right;
        public static Complex Subtract(double left, Complex right) => left - right;
        public static Complex Multiply(Complex left, Complex right) => left * right;
        public static Complex Multiply(Complex left, double right) => left * right;
        public static Complex Multiply(double left, Complex right) => left * right;
        public static Complex Divide(Complex dividend, Complex divisor) => dividend / divisor;
        public static Complex Divide(Complex dividend, double divisor) => dividend / divisor;
        public static Complex Divide(double dividend, Complex divisor) => dividend / divisor;

        public static Complex operator +(Complex value) => value;
        public static Complex operator -(Complex value) => new Complex(-value.m_real, -value.m_imaginary);

        public static Complex operator +(Complex left, Complex right) =>
            new Complex(left.m_real + right.m_real, left.m_imaginary + right.m_imaginary);
        public static Complex operator +(Complex left, double right) =>
            new Complex(left.m_real + right, left.m_imaginary);
        public static Complex operator +(double left, Complex right) =>
            new Complex(left + right.m_real, right.m_imaginary);

        public static Complex operator -(Complex left, Complex right) =>
            new Complex(left.m_real - right.m_real, left.m_imaginary - right.m_imaginary);
        public static Complex operator -(Complex left, double right) =>
            new Complex(left.m_real - right, left.m_imaginary);
        public static Complex operator -(double left, Complex right) =>
            new Complex(left - right.m_real, -right.m_imaginary);

        public static Complex operator *(Complex left, Complex right) =>
            new Complex(left.m_real * right.m_real - left.m_imaginary * right.m_imaginary,
                        left.m_imaginary * right.m_real + left.m_real * right.m_imaginary);
        public static Complex operator *(Complex left, double right)
        {
            if (!double.IsFinite(left.m_real))
            {
                if (!double.IsFinite(left.m_imaginary))
                    return new Complex(double.NaN, double.NaN);
                return new Complex(left.m_real * right, double.NaN);
            }
            if (!double.IsFinite(left.m_imaginary))
                return new Complex(double.NaN, left.m_imaginary * right);
            return new Complex(left.m_real * right, left.m_imaginary * right);
        }
        public static Complex operator *(double left, Complex right) => right * left;

        // Smith's algorithm: divides by the larger part first, so the
        // intermediate products don't overflow or underflow.
        public static Complex operator /(Complex left, Complex right)
        {
            double a = left.m_real, b = left.m_imaginary;
            double c = right.m_real, d = right.m_imaginary;
            if (Math.Abs(d) < Math.Abs(c))
            {
                double doc = d / c;
                return new Complex((a + b * doc) / (c + d * doc), (b - a * doc) / (c + d * doc));
            }
            double cod = c / d;
            return new Complex((b + a * cod) / (d + c * cod), (-a + b * cod) / (d + c * cod));
        }

        public static Complex operator /(Complex left, double right)
        {
            if (right == 0)
                return new Complex(double.NaN, double.NaN);
            if (!double.IsFinite(left.m_real))
            {
                if (!double.IsFinite(left.m_imaginary))
                    return new Complex(double.NaN, double.NaN);
                return new Complex(left.m_real / right, double.NaN);
            }
            if (!double.IsFinite(left.m_imaginary))
                return new Complex(double.NaN, left.m_imaginary / right);
            return new Complex(left.m_real / right, left.m_imaginary / right);
        }

        public static Complex operator /(double left, Complex right)
        {
            double a = left;
            double c = right.m_real, d = right.m_imaginary;
            if (Math.Abs(d) < Math.Abs(c))
            {
                double doc = d / c;
                return new Complex(a / (c + d * doc), (-a * doc) / (c + d * doc));
            }
            double cod = c / d;
            return new Complex(a * cod / (d + c * cod), -a / (d + c * cod));
        }

        public static bool operator ==(Complex left, Complex right) =>
            left.m_real == right.m_real && left.m_imaginary == right.m_imaginary;
        public static bool operator !=(Complex left, Complex right) =>
            left.m_real != right.m_real || left.m_imaginary != right.m_imaginary;

        public static implicit operator Complex(double value) => new Complex(value, 0);
        public static implicit operator Complex(float value) => new Complex(value, 0);
        public static implicit operator Complex(long value) => new Complex(value, 0);
        public static implicit operator Complex(ulong value) => new Complex(value, 0);
        public static implicit operator Complex(int value) => new Complex(value, 0);
        public static implicit operator Complex(uint value) => new Complex(value, 0);
        public static implicit operator Complex(short value) => new Complex(value, 0);
        public static implicit operator Complex(ushort value) => new Complex(value, 0);
        public static implicit operator Complex(sbyte value) => new Complex(value, 0);
        public static implicit operator Complex(byte value) => new Complex(value, 0);
        public static implicit operator Complex(char value) => new Complex(value, 0);
        public static explicit operator Complex(decimal value) => new Complex((double)value, 0);

        // The magnitude, without overflow when the parts are large.
        public static double Abs(Complex value) => Hypot(value.m_real, value.m_imaginary);

        // sqrt(x*x + y*y), within about an ulp. The squares are formed with
        // their rounding errors (each number split into two halves of 26
        // bits, whose products are exact) and the sum scaled by 2^±600 when
        // the squares would overflow or underflow.
        private static unsafe double Hypot(double x, double y)
        {
            x = Math.Abs(x);
            y = Math.Abs(y);
            if (double.IsInfinity(x) || double.IsInfinity(y))
                return double.PositiveInfinity;
            if (x != x || y != y)
                return double.NaN;
            if (x < y)
            {
                double t = x;
                x = y;
                y = t;
            }
            if (y == 0)
                return x;
            if (x > y * 1e17)
                return x + y; // y's square is below x's last digit

            double scale = 1;
            if (x > 1e150)
            {
                x *= 2.409919865102884e-181; // 2^-600
                y *= 2.409919865102884e-181;
                scale = 4.149515568880993e180;
            }
            else if (y < 1e-150)
            {
                x *= 4.149515568880993e180;
                y *= 4.149515568880993e180;
                scale = 2.409919865102884e-181;
            }
            double xx = x * x, yy = y * y;
            double xHead = Head(x), xTail = x - xHead;
            double yHead = Head(y), yTail = y - yHead;
            double xxError = ((xHead * xHead - xx) + 2 * xHead * xTail) + xTail * xTail;
            double yyError = ((yHead * yHead - yy) + 2 * yHead * yTail) + yTail * yTail;
            return Math.Sqrt(xx + (yy + (xxError + yyError))) * scale;
        }

        // The upper 26 bits of the significand.
        private static unsafe double Head(double value)
        {
            ulong bits = *(ulong*)&value & 0xFFFFFFFFF8000000;
            return *(double*)&bits;
        }

        public static Complex Conjugate(Complex value) => new Complex(value.m_real, -value.m_imaginary);

        public static Complex Reciprocal(Complex value)
        {
            if (value.m_real == 0 && value.m_imaginary == 0)
                return Zero;
            return One / value;
        }

        public static Complex Sin(Complex value)
        {
            double a = value.m_real, b = value.m_imaginary;
            return new Complex(Math.Sin(a) * Math.Cosh(b), Math.Cos(a) * Math.Sinh(b));
        }

        public static Complex Cos(Complex value)
        {
            double a = value.m_real, b = value.m_imaginary;
            return new Complex(Math.Cos(a) * Math.Cosh(b), -Math.Sin(a) * Math.Sinh(b));
        }

        public static Complex Sinh(Complex value)
        {
            double a = value.m_real, b = value.m_imaginary;
            return new Complex(Math.Sinh(a) * Math.Cos(b), Math.Cosh(a) * Math.Sin(b));
        }

        public static Complex Cosh(Complex value)
        {
            double a = value.m_real, b = value.m_imaginary;
            return new Complex(Math.Cosh(a) * Math.Cos(b), Math.Sinh(a) * Math.Sin(b));
        }

        // tan(x + yi) = (sin 2x + i sinh 2y) / (cos 2x + cosh 2y), and the
        // denominator is 2 (cos²x + sinh²y), which unlike the sum doesn't
        // cancel near the poles: there .NET's result is off in the fourth
        // digit, this one in the last few. For large |y| sinh and cosh
        // overflow, so .NET's form divided through by cosh 2y is used.
        public static Complex Tan(Complex value)
        {
            double x = value.m_real, y = value.m_imaginary;
            if (Math.Abs(y) <= 4)
            {
                double sx = Math.Sin(x), cx = Math.Cos(x);
                double sh = Math.Sinh(y), ch = Math.Cosh(y);
                double d = cx * cx + sh * sh;
                return new Complex(sx * cx / d, sh * ch / d);
            }
            double x2 = 2 * x, y2 = 2 * y;
            double coshY2 = Math.Cosh(y2);
            double d2 = 1 + Math.Cos(x2) / coshY2;
            return new Complex(Math.Sin(x2) / coshY2 / d2, Math.Tanh(y2) / d2);
        }

        // tanh(z) = -i tan(iz).
        public static Complex Tanh(Complex value)
        {
            Complex t = Tan(new Complex(-value.m_imaginary, value.m_real));
            return new Complex(t.m_imaginary, -t.m_real);
        }

        // Both are computed for |Re z| and |Im z| (the first quadrant) and the
        // signs put back with .NET's rules, which differ from C's on the
        // real axis: Acos(2) = (0, +1.317) there, -1.317 in C.
        public static Complex Asin(Complex value)
        {
            FirstQuadrant(Math.Abs(value.m_real), Math.Abs(value.m_imaginary), out double u, out _, out double v);
            if (value.m_real < 0)
                u = -u;
            if (value.m_imaginary < 0)
                v = -v;
            return new Complex(u, v);
        }

        public static Complex Acos(Complex value)
        {
            FirstQuadrant(Math.Abs(value.m_real), Math.Abs(value.m_imaginary), out _, out double u, out double v);
            if (value.m_real < 0)
                u = Math.PI - u;
            if (value.m_imaginary > 0)
                v = -v;
            return new Complex(u, v);
        }

        // For x, y >= 0: asin(x + yi) = asinU + vi and acos(x + yi) =
        // acosU - vi, by Kahan's formulas, with s1 = sqrt(1 - z) and
        // s2 = sqrt(1 + z): asinU = atan(x / Re(s1 s2)),
        // acosU = 2 atan(Re s1 / Re s2), v = asinh(Im(conj(s1) s2)).
        // Beyond sqrt(MaxValue) / 2 they overflow, and .NET's asymptotic
        // forms take over: there asin(z) ~ -i log(2iz).
        private static void FirstQuadrant(double x, double y, out double asinU, out double acosU, out double v)
        {
            const double OverflowThreshold = 6.703903964971298e153;
            if (x > OverflowThreshold || y > OverflowThreshold)
            {
                double small = x < y ? x : y;
                double big = x < y ? y : x;
                double ratio = small / big;
                asinU = Math.Atan(x / y);
                acosU = Math.Atan(y / x);
                v = Ln2 + Math.Log(big) + 0.5 * Log1p(ratio * ratio);
                return;
            }
            Complex s1 = Sqrt(new Complex(1 - x, -y));
            Complex s2 = Sqrt(new Complex(1 + x, y));
            asinU = Math.Atan2(x, s1.m_real * s2.m_real - s1.m_imaginary * s2.m_imaginary);
            acosU = 2 * Math.Atan2(s1.m_real, s2.m_real);
            v = Math.Abs(Math.Asinh(s1.m_real * s2.m_imaginary - s1.m_imaginary * s2.m_real));
        }

        private const double Ln2 = 0.693147180559945309417;

        private static double Log1p(double x)
        {
            double u = 1 + x;
            if (u == 1)
                return x;
            if (double.IsPositiveInfinity(u))
                return u;
            return Math.Log(u) * x / (u - 1);
        }

        // atan(x + yi) = atan2(2x, 1 - x² - y²) / 2 + i log1p(4y / (x² + (1 - y)²)) / 4,
        // which doesn't cancel for small z the way .NET's
        // (i/2) (log(1 - iz) - log(1 + iz)) does. Same branch cuts.
        public static Complex Atan(Complex value)
        {
            double x = value.m_real, y = value.m_imaginary;
            // On the branch cuts (x = ±0, |y| > 1) .NET takes the real part's
            // sign from y, not from the sign of the zero as C does.
            double side = x == 0 ? y : x;
            // The limit, where the formulas give ∞ - ∞.
            if (double.IsInfinity(x) || double.IsInfinity(y))
            {
                if (double.IsNaN(x) || double.IsNaN(y))
                    return new Complex(double.NaN, 0);
                return new Complex(Math.CopySign(Math.PI / 2, side), Math.CopySign(0, y));
            }
            const double Large = 1e150;
            if (Math.Abs(x) > Large || Math.Abs(y) > Large)
            {
                // atan(z) = ±π/2 - 1/z there; 1/z's imaginary part is
                // -y / |z|², of which the real part keeps only the sign.
                double r = 1 / Hypot(x, y);
                return new Complex(Math.CopySign(Math.PI / 2, side), y * r * r);
            }
            double real = x == 0 && Math.Abs(y) > 1
                ? Math.CopySign(Math.PI / 2, y)
                : Math.Atan2(2 * x, (1 - y) * (1 + y) - x * x) / 2;
            // The log of (x² + (1 + y)²) / (x² + (1 - y)²): by log1p when
            // that is near 1, directly otherwise (near ±i, where it is near 0
            // or huge and log1p's argument would round to -1).
            double oneMinusY = 1 - y, onePlusY = 1 + y;
            double below = x * x + oneMinusY * oneMinusY;
            double t = 4 * y / below;
            double imaginary = Math.Abs(t) < 0.5
                ? Log1p(t) / 4
                : (Math.Log(Hypot(x, onePlusY)) - Math.Log(Hypot(x, oneMinusY))) / 2; // no x² to underflow
            return new Complex(real, imaginary);
        }

        public static Complex Log(Complex value) =>
            new Complex(Math.Log(Abs(value)), Math.Atan2(value.m_imaginary, value.m_real));

        public static Complex Log(Complex value, double baseValue) => Log(value) / Log(baseValue);

        public static Complex Log10(Complex value)
        {
            Complex log = Log(value);
            const double InverseOfLog10 = 0.43429448190325182765;
            return new Complex(log.m_real * InverseOfLog10, log.m_imaginary * InverseOfLog10);
        }

        public static Complex Exp(Complex value)
        {
            double e = Math.Exp(value.m_real);
            return new Complex(e * Math.Cos(value.m_imaginary), e * Math.Sin(value.m_imaginary));
        }

        // The principal square root, whose real part is never negative. The
        // magnitude is taken of a value scaled by 1/4 when the parts are large,
        // so it doesn't overflow.
        public static Complex Sqrt(Complex value)
        {
            double re = value.m_real, im = value.m_imaginary;
            if (im == 0)
            {
                if (re < 0)
                    return new Complex(0, Math.Sqrt(-re));
                return new Complex(Math.Sqrt(re), im);
            }
            if (double.IsInfinity(im))
                return new Complex(double.PositiveInfinity, im);
            if (double.IsNegativeInfinity(re))
                return new Complex(double.IsNaN(im) ? im : 0, Math.CopySign(double.PositiveInfinity, im));
            if (double.IsPositiveInfinity(re))
                return new Complex(re, double.IsNaN(im) ? im : Math.CopySign(0, im));

            const double Large = 1e300;
            bool scaled = Math.Abs(re) > Large || Math.Abs(im) > Large;
            if (scaled)
            {
                re /= 4;
                im /= 4;
            }
            double m = Hypot(re, im);
            double t;
            Complex result;
            if (re >= 0)
            {
                t = Math.Sqrt((m + re) / 2);
                result = new Complex(t, im / (2 * t));
            }
            else
            {
                t = Math.Sqrt((m - re) / 2);
                result = new Complex(Math.Abs(im) / (2 * t), Math.CopySign(t, im));
            }
            return scaled ? new Complex(result.m_real * 2, result.m_imaginary * 2) : result;
        }

        public static Complex Pow(Complex value, Complex power)
        {
            if (power == Zero)
                return One;
            if (value == Zero)
                return Zero;

            double vr = value.m_real, vi = value.m_imaginary;
            double pr = power.m_real, pi = power.m_imaginary;
            double rho = Abs(value);
            double theta = Math.Atan2(vi, vr);
            double newRho = pr * theta + pi * Math.Log(rho);
            double t = Math.Pow(rho, pr) * Math.Exp(-pi * theta);
            return new Complex(t * Math.Cos(newRho), t * Math.Sin(newRho));
        }

        public static Complex Pow(Complex value, double power) => Pow(value, new Complex(power, 0));

        public static bool IsFinite(Complex value) => double.IsFinite(value.m_real) && double.IsFinite(value.m_imaginary);
        public static bool IsInfinity(Complex value) => double.IsInfinity(value.m_real) || double.IsInfinity(value.m_imaginary);
        public static bool IsNaN(Complex value) => !IsInfinity(value) && !IsFinite(value);
        public static bool IsRealNumber(Complex value) => value.m_imaginary == 0 && !double.IsNaN(value.m_real);
        public static bool IsImaginaryNumber(Complex value) => value.m_real == 0 && !double.IsNaN(value.m_imaginary);

        public bool Equals(Complex value) => m_real.Equals(value.m_real) && m_imaginary.Equals(value.m_imaginary);
        public override bool Equals(object obj) => obj is Complex other && Equals(other);

        public override int GetHashCode()
        {
            const int N = 99999997;
            int realHash = m_real.GetHashCode() % N;
            int imaginaryHash = m_imaginary.GetHashCode();
            return realHash ^ imaginaryHash;
        }

        // .NET's format: <real; imaginary>.
        public override string ToString() =>
            string.Concat("<", m_real.ToString(), "; ", m_imaginary.ToString()) + ">";
    }
}

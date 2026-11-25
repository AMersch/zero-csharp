namespace System.Numerics
{
    // A 2D transform (rotation, scale, skew and translation) with .NET's
    // members. Row vectors, as in .NET: a point is transformed as
    // [x y 1] * M, so M31 and M32 are the translation.
    public struct Matrix3x2 : IEquatable<Matrix3x2>
    {
        public float M11, M12;
        public float M21, M22;
        public float M31, M32;

        public Matrix3x2(float m11, float m12, float m21, float m22, float m31, float m32)
        {
            M11 = m11;
            M12 = m12;
            M21 = m21;
            M22 = m22;
            M31 = m31;
            M32 = m32;
        }

        public static Matrix3x2 Identity => new Matrix3x2(1, 0, 0, 1, 0, 0);

        public readonly bool IsIdentity => M11 == 1 && M12 == 0 && M21 == 0 && M22 == 1 && M31 == 0 && M32 == 0;

        public Vector2 Translation
        {
            readonly get => new Vector2(M31, M32);
            set
            {
                M31 = value.X;
                M32 = value.Y;
            }
        }

        public float this[int row, int column]
        {
            readonly get
            {
                switch (row * 2 + column)
                {
                    case 0 when column < 2: return M11;
                    case 1 when column < 2: return M12;
                    case 2 when column < 2: return M21;
                    case 3 when column < 2: return M22;
                    case 4 when column < 2: return M31;
                    case 5 when column < 2: return M32;
                    default: throw new ArgumentOutOfRangeException(nameof(row));
                }
            }
            set
            {
                switch (row * 2 + column)
                {
                    case 0 when column < 2: M11 = value; break;
                    case 1 when column < 2: M12 = value; break;
                    case 2 when column < 2: M21 = value; break;
                    case 3 when column < 2: M22 = value; break;
                    case 4 when column < 2: M31 = value; break;
                    case 5 when column < 2: M32 = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(row));
                }
            }
        }

        public static Matrix3x2 CreateTranslation(Vector2 position) => CreateTranslation(position.X, position.Y);
        public static Matrix3x2 CreateTranslation(float xPosition, float yPosition) =>
            new Matrix3x2(1, 0, 0, 1, xPosition, yPosition);

        public static Matrix3x2 CreateScale(float xScale, float yScale) => new Matrix3x2(xScale, 0, 0, yScale, 0, 0);
        public static Matrix3x2 CreateScale(Vector2 scales) => CreateScale(scales.X, scales.Y);
        public static Matrix3x2 CreateScale(float scale) => CreateScale(scale, scale);
        public static Matrix3x2 CreateScale(float scale, Vector2 centerPoint) => CreateScale(scale, scale, centerPoint);
        public static Matrix3x2 CreateScale(Vector2 scales, Vector2 centerPoint) => CreateScale(scales.X, scales.Y, centerPoint);

        // Scales about centerPoint, which stays where it is.
        public static Matrix3x2 CreateScale(float xScale, float yScale, Vector2 centerPoint) =>
            new Matrix3x2(xScale, 0, 0, yScale,
                          centerPoint.X * (1 - xScale), centerPoint.Y * (1 - yScale));

        public static Matrix3x2 CreateSkew(float radiansX, float radiansY) =>
            new Matrix3x2(1, MathF.Tan(radiansY), MathF.Tan(radiansX), 1, 0, 0);

        public static Matrix3x2 CreateSkew(float radiansX, float radiansY, Vector2 centerPoint)
        {
            float xTan = MathF.Tan(radiansX);
            float yTan = MathF.Tan(radiansY);
            return new Matrix3x2(1, yTan, xTan, 1, -centerPoint.Y * xTan, -centerPoint.X * yTan);
        }

        public static Matrix3x2 CreateRotation(float radians) => CreateRotation(radians, default);

        // As in .NET, angles within a thousandth of a degree of a quarter turn
        // get exact sines and cosines (0 and ±1), so a rotation by 90 degrees
        // maps whole numbers to whole numbers.
        public static Matrix3x2 CreateRotation(float radians, Vector2 centerPoint)
        {
            radians = MathF.IEEERemainder(radians, MathF.PI * 2);
            const float Epsilon = 0.001f * MathF.PI / 180f;
            float c, s;
            if (radians > -Epsilon && radians < Epsilon)
            {
                c = 1;
                s = 0;
            }
            else if (radians > MathF.PI / 2 - Epsilon && radians < MathF.PI / 2 + Epsilon)
            {
                c = 0;
                s = 1;
            }
            else if (radians < -MathF.PI + Epsilon || radians > MathF.PI - Epsilon)
            {
                c = -1;
                s = 0;
            }
            else if (radians > -MathF.PI / 2 - Epsilon && radians < -MathF.PI / 2 + Epsilon)
            {
                c = 0;
                s = -1;
            }
            else
            {
                c = MathF.Cos(radians);
                s = MathF.Sin(radians);
            }
            float x = centerPoint.X * (1 - c) + centerPoint.Y * s;
            float y = centerPoint.Y * (1 - c) - centerPoint.X * s;
            return new Matrix3x2(c, s, -s, c, x, y);
        }

        public readonly float GetDeterminant() => M11 * M22 - M21 * M12;

        // False, with every element NaN, when the matrix has no inverse.
        public static bool Invert(Matrix3x2 matrix, out Matrix3x2 result)
        {
            float det = matrix.GetDeterminant();
            if (MathF.Abs(det) < float.Epsilon)
            {
                result = new Matrix3x2(float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN);
                return false;
            }
            float invDet = 1 / det;
            result = new Matrix3x2(
                matrix.M22 * invDet,
                -matrix.M12 * invDet,
                -matrix.M21 * invDet,
                matrix.M11 * invDet,
                (matrix.M21 * matrix.M32 - matrix.M31 * matrix.M22) * invDet,
                (matrix.M31 * matrix.M12 - matrix.M11 * matrix.M32) * invDet);
            return true;
        }

        public static Matrix3x2 Lerp(Matrix3x2 matrix1, Matrix3x2 matrix2, float amount) =>
            matrix1 + (matrix2 - matrix1) * amount;

        public static Matrix3x2 operator +(Matrix3x2 value1, Matrix3x2 value2) =>
            new Matrix3x2(value1.M11 + value2.M11, value1.M12 + value2.M12,
                          value1.M21 + value2.M21, value1.M22 + value2.M22,
                          value1.M31 + value2.M31, value1.M32 + value2.M32);

        public static Matrix3x2 operator -(Matrix3x2 value1, Matrix3x2 value2) =>
            new Matrix3x2(value1.M11 - value2.M11, value1.M12 - value2.M12,
                          value1.M21 - value2.M21, value1.M22 - value2.M22,
                          value1.M31 - value2.M31, value1.M32 - value2.M32);

        public static Matrix3x2 operator -(Matrix3x2 value) =>
            new Matrix3x2(-value.M11, -value.M12, -value.M21, -value.M22, -value.M31, -value.M32);

        public static Matrix3x2 operator *(Matrix3x2 value1, float value2) =>
            new Matrix3x2(value1.M11 * value2, value1.M12 * value2,
                          value1.M21 * value2, value1.M22 * value2,
                          value1.M31 * value2, value1.M32 * value2);

        // value1 first, then value2.
        public static Matrix3x2 operator *(Matrix3x2 value1, Matrix3x2 value2) =>
            new Matrix3x2(
                value1.M11 * value2.M11 + value1.M12 * value2.M21,
                value1.M11 * value2.M12 + value1.M12 * value2.M22,
                value1.M21 * value2.M11 + value1.M22 * value2.M21,
                value1.M21 * value2.M12 + value1.M22 * value2.M22,
                value1.M31 * value2.M11 + value1.M32 * value2.M21 + value2.M31,
                value1.M31 * value2.M12 + value1.M32 * value2.M22 + value2.M32);

        public static bool operator ==(Matrix3x2 value1, Matrix3x2 value2) =>
            value1.M11 == value2.M11 && value1.M12 == value2.M12 &&
            value1.M21 == value2.M21 && value1.M22 == value2.M22 &&
            value1.M31 == value2.M31 && value1.M32 == value2.M32;

        public static bool operator !=(Matrix3x2 value1, Matrix3x2 value2) => !(value1 == value2);

        public static Matrix3x2 Add(Matrix3x2 value1, Matrix3x2 value2) => value1 + value2;
        public static Matrix3x2 Subtract(Matrix3x2 value1, Matrix3x2 value2) => value1 - value2;
        public static Matrix3x2 Multiply(Matrix3x2 value1, Matrix3x2 value2) => value1 * value2;
        public static Matrix3x2 Multiply(Matrix3x2 value1, float value2) => value1 * value2;
        public static Matrix3x2 Negate(Matrix3x2 value) => -value;

        public readonly bool Equals(Matrix3x2 other) =>
            M11.Equals(other.M11) && M12.Equals(other.M12) &&
            M21.Equals(other.M21) && M22.Equals(other.M22) &&
            M31.Equals(other.M31) && M32.Equals(other.M32);

        public override readonly bool Equals(object obj) => obj is Matrix3x2 other && Equals(other);

        public override readonly int GetHashCode()
        {
            HashCode hash = default;
            hash.Add(M11);
            hash.Add(M12);
            hash.Add(M21);
            hash.Add(M22);
            hash.Add(M31);
            hash.Add(M32);
            return hash.ToHashCode();
        }

        // .NET's format: { {M11:1 M12:0} {M21:0 M22:1} {M31:0 M32:0} }.
        public override readonly string ToString() =>
            string.Concat(new string[]
            {
                "{ {M11:", M11.ToString(), " M12:", M12.ToString(),
                "} {M21:", M21.ToString(), " M22:", M22.ToString(),
                "} {M31:", M31.ToString(), " M32:", M32.ToString(), "} }",
            });
    }
}

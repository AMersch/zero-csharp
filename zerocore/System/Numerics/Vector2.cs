namespace System.Numerics
{
    // A vector of 2 floats with .NET's members. A plain struct: .NET
    // treats this type as a SIMD register, which zero mode's compiler does only
    // for types marked [Intrinsic], so here the operations are scalar code that
    // works on every target. The compiler still vectorizes some of it.
    public struct Vector2 : IEquatable<Vector2>
    {
        public float X;
        public float Y;

        public Vector2(float value)
        {
            X = value;
            Y = value;
        }

        public Vector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public Vector2(ReadOnlySpan<float> values)
        {
            if (values.Length < 2)
                throw new ArgumentOutOfRangeException(nameof(values));
            X = values[0];
            Y = values[1];
        }

        public static Vector2 Zero => default;
        public static Vector2 One => new Vector2(1);
        public static Vector2 UnitX => new Vector2(1, 0);
        public static Vector2 UnitY => new Vector2(0, 1);

        public float this[int index]
        {
            readonly get
            {
                switch (index)
                {
                    case 0: return X;
                    case 1: return Y;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
            set
            {
                switch (index)
                {
                    case 0: X = value; break;
                    case 1: Y = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public static Vector2 operator +(Vector2 left, Vector2 right) =>
            new Vector2(left.X + right.X, left.Y + right.Y);
        public static Vector2 operator -(Vector2 left, Vector2 right) =>
            new Vector2(left.X - right.X, left.Y - right.Y);
        public static Vector2 operator *(Vector2 left, Vector2 right) =>
            new Vector2(left.X * right.X, left.Y * right.Y);
        public static Vector2 operator *(Vector2 left, float right) =>
            new Vector2(left.X * right, left.Y * right);
        public static Vector2 operator *(float left, Vector2 right) =>
            new Vector2(left * right.X, left * right.Y);
        public static Vector2 operator /(Vector2 left, Vector2 right) =>
            new Vector2(left.X / right.X, left.Y / right.Y);
        public static Vector2 operator /(Vector2 value1, float value2) =>
            new Vector2(value1.X / value2, value1.Y / value2);
        public static Vector2 operator -(Vector2 value) => new Vector2(-value.X, -value.Y);
        public static bool operator ==(Vector2 left, Vector2 right) =>
            left.X == right.X && left.Y == right.Y;
        public static bool operator !=(Vector2 left, Vector2 right) => !(left == right);

        public static Vector2 Add(Vector2 left, Vector2 right) => left + right;
        public static Vector2 Subtract(Vector2 left, Vector2 right) => left - right;
        public static Vector2 Multiply(Vector2 left, Vector2 right) => left * right;
        public static Vector2 Multiply(Vector2 left, float right) => left * right;
        public static Vector2 Multiply(float left, Vector2 right) => left * right;
        public static Vector2 Divide(Vector2 left, Vector2 right) => left / right;
        public static Vector2 Divide(Vector2 left, float divisor) => left / divisor;
        public static Vector2 Negate(Vector2 value) => -value;

        public static Vector2 Abs(Vector2 value) => new Vector2(MathF.Abs(value.X), MathF.Abs(value.Y));
        public static Vector2 SquareRoot(Vector2 value) => new Vector2(MathF.Sqrt(value.X), MathF.Sqrt(value.Y));
        public static Vector2 Min(Vector2 value1, Vector2 value2) =>
            new Vector2(value1.X < value2.X ? value1.X : value2.X, value1.Y < value2.Y ? value1.Y : value2.Y);
        public static Vector2 Max(Vector2 value1, Vector2 value2) =>
            new Vector2(value1.X > value2.X ? value1.X : value2.X, value1.Y > value2.Y ? value1.Y : value2.Y);
        public static Vector2 Clamp(Vector2 value1, Vector2 min, Vector2 max) => Min(Max(value1, min), max);

        public static float Dot(Vector2 vector1, Vector2 vector2) =>
            vector1.X * vector2.X + vector1.Y * vector2.Y;
        public readonly float LengthSquared() => Dot(this, this);
        public readonly float Length() => MathF.Sqrt(LengthSquared());
        public static float DistanceSquared(Vector2 value1, Vector2 value2) => (value1 - value2).LengthSquared();
        public static float Distance(Vector2 value1, Vector2 value2) => MathF.Sqrt(DistanceSquared(value1, value2));
        public static Vector2 Normalize(Vector2 value) => value / value.Length();
        public static Vector2 Lerp(Vector2 value1, Vector2 value2, float amount) =>
            value1 * (1 - amount) + value2 * amount;

        // The reflection of a vector off a surface with the given normal.
        public static Vector2 Reflect(Vector2 vector, Vector2 normal) => vector - 2 * Dot(vector, normal) * normal;

        public readonly void CopyTo(float[] array) => CopyTo(array, 0);

        public readonly void CopyTo(float[] array, int index)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            if (index < 0 || array.Length - index < 2)
                throw new ArgumentOutOfRangeException(nameof(index));
            array[index + 0] = X;
            array[index + 1] = Y;
        }

        public readonly void CopyTo(Span<float> destination)
        {
            if (!TryCopyTo(destination))
                throw new ArgumentException("Destination is too short.");
        }

        public readonly bool TryCopyTo(Span<float> destination)
        {
            if (destination.Length < 2)
                return false;
            destination[0] = X;
            destination[1] = Y;
            return true;
        }

        // NaN equals NaN here, unlike with ==, as in .NET.
        public readonly bool Equals(Vector2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override readonly bool Equals(object obj) => obj is Vector2 other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(X, Y);

        // .NET's format: <1, 2>.
        public override readonly string ToString() =>
            string.Concat(new string[] { "<", X.ToString(), ", ", Y.ToString(), ">" });

        public static Vector2 Transform(Vector2 position, Matrix3x2 matrix) =>
            new Vector2(position.X * matrix.M11 + position.Y * matrix.M21 + matrix.M31,
                        position.X * matrix.M12 + position.Y * matrix.M22 + matrix.M32);

        public static Vector2 Transform(Vector2 position, Matrix4x4 matrix) =>
            new Vector2(position.X * matrix.M11 + position.Y * matrix.M21 + matrix.M41,
                        position.X * matrix.M12 + position.Y * matrix.M22 + matrix.M42);

        // A direction: the matrix's translation doesn't apply.
        public static Vector2 TransformNormal(Vector2 normal, Matrix3x2 matrix) =>
            new Vector2(normal.X * matrix.M11 + normal.Y * matrix.M21,
                        normal.X * matrix.M12 + normal.Y * matrix.M22);

        public static Vector2 TransformNormal(Vector2 normal, Matrix4x4 matrix) =>
            new Vector2(normal.X * matrix.M11 + normal.Y * matrix.M21,
                        normal.X * matrix.M12 + normal.Y * matrix.M22);

        public static Vector2 Transform(Vector2 value, Quaternion rotation)
        {
            float x2 = rotation.X + rotation.X;
            float y2 = rotation.Y + rotation.Y;
            float z2 = rotation.Z + rotation.Z;
            float wz2 = rotation.W * z2;
            float xx2 = rotation.X * x2;
            float xy2 = rotation.X * y2;
            float yy2 = rotation.Y * y2;
            float zz2 = rotation.Z * z2;
            return new Vector2(value.X * (1 - yy2 - zz2) + value.Y * (xy2 - wz2),
                               value.X * (xy2 + wz2) + value.Y * (1 - xx2 - zz2));
        }
    }
}

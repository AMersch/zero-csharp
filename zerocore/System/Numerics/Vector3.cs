namespace System.Numerics
{
    // A vector of 3 floats with .NET's members. A plain struct: .NET
    // treats this type as a SIMD register, which zero mode's compiler does only
    // for types marked [Intrinsic], so here the operations are scalar code that
    // works on every target. The compiler still vectorizes some of it.
    public struct Vector3 : IEquatable<Vector3>
    {
        public float X;
        public float Y;
        public float Z;

        public Vector3(float value)
        {
            X = value;
            Y = value;
            Z = value;
        }

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Vector3(Vector2 value, float z)
        {
            X = value.X;
            Y = value.Y;
            Z = z;
        }

        public Vector3(ReadOnlySpan<float> values)
        {
            if (values.Length < 3)
                throw new ArgumentOutOfRangeException(nameof(values));
            X = values[0];
            Y = values[1];
            Z = values[2];
        }

        public static Vector3 Zero => default;
        public static Vector3 One => new Vector3(1);
        public static Vector3 UnitX => new Vector3(1, 0, 0);
        public static Vector3 UnitY => new Vector3(0, 1, 0);
        public static Vector3 UnitZ => new Vector3(0, 0, 1);

        public float this[int index]
        {
            readonly get
            {
                switch (index)
                {
                    case 0: return X;
                    case 1: return Y;
                    case 2: return Z;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
            set
            {
                switch (index)
                {
                    case 0: X = value; break;
                    case 1: Y = value; break;
                    case 2: Z = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public static Vector3 operator +(Vector3 left, Vector3 right) =>
            new Vector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        public static Vector3 operator -(Vector3 left, Vector3 right) =>
            new Vector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        public static Vector3 operator *(Vector3 left, Vector3 right) =>
            new Vector3(left.X * right.X, left.Y * right.Y, left.Z * right.Z);
        public static Vector3 operator *(Vector3 left, float right) =>
            new Vector3(left.X * right, left.Y * right, left.Z * right);
        public static Vector3 operator *(float left, Vector3 right) =>
            new Vector3(left * right.X, left * right.Y, left * right.Z);
        public static Vector3 operator /(Vector3 left, Vector3 right) =>
            new Vector3(left.X / right.X, left.Y / right.Y, left.Z / right.Z);
        public static Vector3 operator /(Vector3 value1, float value2) =>
            new Vector3(value1.X / value2, value1.Y / value2, value1.Z / value2);
        public static Vector3 operator -(Vector3 value) => new Vector3(-value.X, -value.Y, -value.Z);
        public static bool operator ==(Vector3 left, Vector3 right) =>
            left.X == right.X && left.Y == right.Y && left.Z == right.Z;
        public static bool operator !=(Vector3 left, Vector3 right) => !(left == right);

        public static Vector3 Add(Vector3 left, Vector3 right) => left + right;
        public static Vector3 Subtract(Vector3 left, Vector3 right) => left - right;
        public static Vector3 Multiply(Vector3 left, Vector3 right) => left * right;
        public static Vector3 Multiply(Vector3 left, float right) => left * right;
        public static Vector3 Multiply(float left, Vector3 right) => left * right;
        public static Vector3 Divide(Vector3 left, Vector3 right) => left / right;
        public static Vector3 Divide(Vector3 left, float divisor) => left / divisor;
        public static Vector3 Negate(Vector3 value) => -value;

        public static Vector3 Abs(Vector3 value) => new Vector3(MathF.Abs(value.X), MathF.Abs(value.Y), MathF.Abs(value.Z));
        public static Vector3 SquareRoot(Vector3 value) => new Vector3(MathF.Sqrt(value.X), MathF.Sqrt(value.Y), MathF.Sqrt(value.Z));
        public static Vector3 Min(Vector3 value1, Vector3 value2) =>
            new Vector3(value1.X < value2.X ? value1.X : value2.X, value1.Y < value2.Y ? value1.Y : value2.Y, value1.Z < value2.Z ? value1.Z : value2.Z);
        public static Vector3 Max(Vector3 value1, Vector3 value2) =>
            new Vector3(value1.X > value2.X ? value1.X : value2.X, value1.Y > value2.Y ? value1.Y : value2.Y, value1.Z > value2.Z ? value1.Z : value2.Z);
        public static Vector3 Clamp(Vector3 value1, Vector3 min, Vector3 max) => Min(Max(value1, min), max);

        public static float Dot(Vector3 vector1, Vector3 vector2) =>
            vector1.X * vector2.X + vector1.Y * vector2.Y + vector1.Z * vector2.Z;
        public readonly float LengthSquared() => Dot(this, this);
        public readonly float Length() => MathF.Sqrt(LengthSquared());
        public static float DistanceSquared(Vector3 value1, Vector3 value2) => (value1 - value2).LengthSquared();
        public static float Distance(Vector3 value1, Vector3 value2) => MathF.Sqrt(DistanceSquared(value1, value2));
        public static Vector3 Normalize(Vector3 value) => value / value.Length();
        public static Vector3 Lerp(Vector3 value1, Vector3 value2, float amount) =>
            value1 * (1 - amount) + value2 * amount;

        // The reflection of a vector off a surface with the given normal.
        public static Vector3 Reflect(Vector3 vector, Vector3 normal) => vector - 2 * Dot(vector, normal) * normal;

        public readonly void CopyTo(float[] array) => CopyTo(array, 0);

        public readonly void CopyTo(float[] array, int index)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            if (index < 0 || array.Length - index < 3)
                throw new ArgumentOutOfRangeException(nameof(index));
            array[index + 0] = X;
            array[index + 1] = Y;
            array[index + 2] = Z;
        }

        public readonly void CopyTo(Span<float> destination)
        {
            if (!TryCopyTo(destination))
                throw new ArgumentException("Destination is too short.");
        }

        public readonly bool TryCopyTo(Span<float> destination)
        {
            if (destination.Length < 3)
                return false;
            destination[0] = X;
            destination[1] = Y;
            destination[2] = Z;
            return true;
        }

        // NaN equals NaN here, unlike with ==, as in .NET.
        public readonly bool Equals(Vector3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override readonly bool Equals(object obj) => obj is Vector3 other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z);

        // .NET's format: <1, 2>.
        public override readonly string ToString() =>
            string.Concat(new string[] { "<", X.ToString(), ", ", Y.ToString(), ", ", Z.ToString(), ">" });

        public static Vector3 Cross(Vector3 vector1, Vector3 vector2) =>
            new Vector3(vector1.Y * vector2.Z - vector1.Z * vector2.Y,
                        vector1.Z * vector2.X - vector1.X * vector2.Z,
                        vector1.X * vector2.Y - vector1.Y * vector2.X);

        public static Vector3 Transform(Vector3 position, Matrix4x4 matrix) =>
            new Vector3(position.X * matrix.M11 + position.Y * matrix.M21 + position.Z * matrix.M31 + matrix.M41,
                        position.X * matrix.M12 + position.Y * matrix.M22 + position.Z * matrix.M32 + matrix.M42,
                        position.X * matrix.M13 + position.Y * matrix.M23 + position.Z * matrix.M33 + matrix.M43);

        // A direction: the matrix's translation doesn't apply.
        public static Vector3 TransformNormal(Vector3 normal, Matrix4x4 matrix) =>
            new Vector3(normal.X * matrix.M11 + normal.Y * matrix.M21 + normal.Z * matrix.M31,
                        normal.X * matrix.M12 + normal.Y * matrix.M22 + normal.Z * matrix.M32,
                        normal.X * matrix.M13 + normal.Y * matrix.M23 + normal.Z * matrix.M33);

        public static Vector3 Transform(Vector3 value, Quaternion rotation)
        {
            float x2 = rotation.X + rotation.X;
            float y2 = rotation.Y + rotation.Y;
            float z2 = rotation.Z + rotation.Z;
            float wx2 = rotation.W * x2;
            float wy2 = rotation.W * y2;
            float wz2 = rotation.W * z2;
            float xx2 = rotation.X * x2;
            float xy2 = rotation.X * y2;
            float xz2 = rotation.X * z2;
            float yy2 = rotation.Y * y2;
            float yz2 = rotation.Y * z2;
            float zz2 = rotation.Z * z2;
            return new Vector3(
                value.X * (1 - yy2 - zz2) + value.Y * (xy2 - wz2) + value.Z * (xz2 + wy2),
                value.X * (xy2 + wz2) + value.Y * (1 - xx2 - zz2) + value.Z * (yz2 - wx2),
                value.X * (xz2 - wy2) + value.Y * (yz2 + wx2) + value.Z * (1 - xx2 - yy2));
        }
    }
}

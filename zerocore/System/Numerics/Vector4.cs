namespace System.Numerics
{
    // A vector of 4 floats with .NET's members. A plain struct: .NET
    // treats this type as a SIMD register, which zero mode's compiler does only
    // for types marked [Intrinsic], so here the operations are scalar code that
    // works on every target. The compiler still vectorizes some of it.
    public struct Vector4 : IEquatable<Vector4>
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public Vector4(float value)
        {
            X = value;
            Y = value;
            Z = value;
            W = value;
        }

        public Vector4(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public Vector4(Vector2 value, float z, float w)
        {
            X = value.X;
            Y = value.Y;
            Z = z;
            W = w;
        }

        public Vector4(Vector3 value, float w)
        {
            X = value.X;
            Y = value.Y;
            Z = value.Z;
            W = w;
        }

        public Vector4(ReadOnlySpan<float> values)
        {
            if (values.Length < 4)
                throw new ArgumentOutOfRangeException(nameof(values));
            X = values[0];
            Y = values[1];
            Z = values[2];
            W = values[3];
        }

        public static Vector4 Zero => default;
        public static Vector4 One => new Vector4(1);
        public static Vector4 UnitX => new Vector4(1, 0, 0, 0);
        public static Vector4 UnitY => new Vector4(0, 1, 0, 0);
        public static Vector4 UnitZ => new Vector4(0, 0, 1, 0);
        public static Vector4 UnitW => new Vector4(0, 0, 0, 1);

        public float this[int index]
        {
            readonly get
            {
                switch (index)
                {
                    case 0: return X;
                    case 1: return Y;
                    case 2: return Z;
                    case 3: return W;
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
                    case 3: W = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public static Vector4 operator +(Vector4 left, Vector4 right) =>
            new Vector4(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);
        public static Vector4 operator -(Vector4 left, Vector4 right) =>
            new Vector4(left.X - right.X, left.Y - right.Y, left.Z - right.Z, left.W - right.W);
        public static Vector4 operator *(Vector4 left, Vector4 right) =>
            new Vector4(left.X * right.X, left.Y * right.Y, left.Z * right.Z, left.W * right.W);
        public static Vector4 operator *(Vector4 left, float right) =>
            new Vector4(left.X * right, left.Y * right, left.Z * right, left.W * right);
        public static Vector4 operator *(float left, Vector4 right) =>
            new Vector4(left * right.X, left * right.Y, left * right.Z, left * right.W);
        public static Vector4 operator /(Vector4 left, Vector4 right) =>
            new Vector4(left.X / right.X, left.Y / right.Y, left.Z / right.Z, left.W / right.W);
        public static Vector4 operator /(Vector4 value1, float value2) =>
            new Vector4(value1.X / value2, value1.Y / value2, value1.Z / value2, value1.W / value2);
        public static Vector4 operator -(Vector4 value) => new Vector4(-value.X, -value.Y, -value.Z, -value.W);
        public static bool operator ==(Vector4 left, Vector4 right) =>
            left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;
        public static bool operator !=(Vector4 left, Vector4 right) => !(left == right);

        public static Vector4 Add(Vector4 left, Vector4 right) => left + right;
        public static Vector4 Subtract(Vector4 left, Vector4 right) => left - right;
        public static Vector4 Multiply(Vector4 left, Vector4 right) => left * right;
        public static Vector4 Multiply(Vector4 left, float right) => left * right;
        public static Vector4 Multiply(float left, Vector4 right) => left * right;
        public static Vector4 Divide(Vector4 left, Vector4 right) => left / right;
        public static Vector4 Divide(Vector4 left, float divisor) => left / divisor;
        public static Vector4 Negate(Vector4 value) => -value;

        public static Vector4 Abs(Vector4 value) => new Vector4(MathF.Abs(value.X), MathF.Abs(value.Y), MathF.Abs(value.Z), MathF.Abs(value.W));
        public static Vector4 SquareRoot(Vector4 value) => new Vector4(MathF.Sqrt(value.X), MathF.Sqrt(value.Y), MathF.Sqrt(value.Z), MathF.Sqrt(value.W));
        public static Vector4 Min(Vector4 value1, Vector4 value2) =>
            new Vector4(value1.X < value2.X ? value1.X : value2.X, value1.Y < value2.Y ? value1.Y : value2.Y, value1.Z < value2.Z ? value1.Z : value2.Z, value1.W < value2.W ? value1.W : value2.W);
        public static Vector4 Max(Vector4 value1, Vector4 value2) =>
            new Vector4(value1.X > value2.X ? value1.X : value2.X, value1.Y > value2.Y ? value1.Y : value2.Y, value1.Z > value2.Z ? value1.Z : value2.Z, value1.W > value2.W ? value1.W : value2.W);
        public static Vector4 Clamp(Vector4 value1, Vector4 min, Vector4 max) => Min(Max(value1, min), max);

        public static float Dot(Vector4 vector1, Vector4 vector2) =>
            vector1.X * vector2.X + vector1.Y * vector2.Y + vector1.Z * vector2.Z + vector1.W * vector2.W;
        public readonly float LengthSquared() => Dot(this, this);
        public readonly float Length() => MathF.Sqrt(LengthSquared());
        public static float DistanceSquared(Vector4 value1, Vector4 value2) => (value1 - value2).LengthSquared();
        public static float Distance(Vector4 value1, Vector4 value2) => MathF.Sqrt(DistanceSquared(value1, value2));
        public static Vector4 Normalize(Vector4 value) => value / value.Length();
        public static Vector4 Lerp(Vector4 value1, Vector4 value2, float amount) =>
            value1 * (1 - amount) + value2 * amount;

        public readonly void CopyTo(float[] array) => CopyTo(array, 0);

        public readonly void CopyTo(float[] array, int index)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            if (index < 0 || array.Length - index < 4)
                throw new ArgumentOutOfRangeException(nameof(index));
            array[index + 0] = X;
            array[index + 1] = Y;
            array[index + 2] = Z;
            array[index + 3] = W;
        }

        public readonly void CopyTo(Span<float> destination)
        {
            if (!TryCopyTo(destination))
                throw new ArgumentException("Destination is too short.");
        }

        public readonly bool TryCopyTo(Span<float> destination)
        {
            if (destination.Length < 4)
                return false;
            destination[0] = X;
            destination[1] = Y;
            destination[2] = Z;
            destination[3] = W;
            return true;
        }

        // NaN equals NaN here, unlike with ==, as in .NET.
        public readonly bool Equals(Vector4 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);
        public override readonly bool Equals(object obj) => obj is Vector4 other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z, W);

        // .NET's format: <1, 2>.
        public override readonly string ToString() =>
            string.Concat(new string[] { "<", X.ToString(), ", ", Y.ToString(), ", ", Z.ToString(), ", ", W.ToString(), ">" });

        public static Vector4 Transform(Vector4 vector, Matrix4x4 matrix) =>
            new Vector4(
                vector.X * matrix.M11 + vector.Y * matrix.M21 + vector.Z * matrix.M31 + vector.W * matrix.M41,
                vector.X * matrix.M12 + vector.Y * matrix.M22 + vector.Z * matrix.M32 + vector.W * matrix.M42,
                vector.X * matrix.M13 + vector.Y * matrix.M23 + vector.Z * matrix.M33 + vector.W * matrix.M43,
                vector.X * matrix.M14 + vector.Y * matrix.M24 + vector.Z * matrix.M34 + vector.W * matrix.M44);

        public static Vector4 Transform(Vector3 position, Matrix4x4 matrix) =>
            Transform(new Vector4(position, 1), matrix);

        public static Vector4 Transform(Vector2 position, Matrix4x4 matrix) =>
            Transform(new Vector4(position, 0, 1), matrix);

        public static Vector4 Transform(Vector4 value, Quaternion rotation) =>
            new Vector4(Vector3.Transform(new Vector3(value.X, value.Y, value.Z), rotation), value.W);

        public static Vector4 Transform(Vector3 value, Quaternion rotation) =>
            new Vector4(Vector3.Transform(value, rotation), 1);

        public static Vector4 Transform(Vector2 value, Quaternion rotation) =>
            new Vector4(Vector3.Transform(new Vector3(value, 0), rotation), 1);
    }
}

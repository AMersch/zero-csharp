namespace System.Numerics
{
    // A rotation in 3D, as x i + y j + z k + w, with .NET's members.
    public struct Quaternion : IEquatable<Quaternion>
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public Quaternion(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public Quaternion(Vector3 vectorPart, float scalarPart)
        {
            X = vectorPart.X;
            Y = vectorPart.Y;
            Z = vectorPart.Z;
            W = scalarPart;
        }

        public static Quaternion Zero => default;
        public static Quaternion Identity => new Quaternion(0, 0, 0, 1);

        public readonly bool IsIdentity => X == 0 && Y == 0 && Z == 0 && W == 1;

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

        // axis must be of length 1.
        public static Quaternion CreateFromAxisAngle(Vector3 axis, float angle)
        {
            float half = angle * 0.5f;
            float s = MathF.Sin(half);
            float c = MathF.Cos(half);
            return new Quaternion(axis.X * s, axis.Y * s, axis.Z * s, c);
        }

        // Yaw about the Y axis, pitch about X, roll about Z.
        public static Quaternion CreateFromYawPitchRoll(float yaw, float pitch, float roll)
        {
            float sr = MathF.Sin(roll * 0.5f), cr = MathF.Cos(roll * 0.5f);
            float sp = MathF.Sin(pitch * 0.5f), cp = MathF.Cos(pitch * 0.5f);
            float sy = MathF.Sin(yaw * 0.5f), cy = MathF.Cos(yaw * 0.5f);
            return new Quaternion(
                cy * sp * cr + sy * cp * sr,
                sy * cp * cr - cy * sp * sr,
                cy * cp * sr - sy * sp * cr,
                cy * cp * cr + sy * sp * sr);
        }

        // From the rotation part of a matrix (the upper 3x3), taking the
        // square root of whichever diagonal term keeps it away from zero.
        public static Quaternion CreateFromRotationMatrix(Matrix4x4 matrix)
        {
            float trace = matrix.M11 + matrix.M22 + matrix.M33;
            Quaternion q;
            if (trace > 0)
            {
                float s = MathF.Sqrt(trace + 1);
                q.W = s * 0.5f;
                s = 0.5f / s;
                q.X = (matrix.M23 - matrix.M32) * s;
                q.Y = (matrix.M31 - matrix.M13) * s;
                q.Z = (matrix.M12 - matrix.M21) * s;
            }
            else if (matrix.M11 >= matrix.M22 && matrix.M11 >= matrix.M33)
            {
                float s = MathF.Sqrt(1 + matrix.M11 - matrix.M22 - matrix.M33);
                float invS = 0.5f / s;
                q.X = 0.5f * s;
                q.Y = (matrix.M12 + matrix.M21) * invS;
                q.Z = (matrix.M13 + matrix.M31) * invS;
                q.W = (matrix.M23 - matrix.M32) * invS;
            }
            else if (matrix.M22 > matrix.M33)
            {
                float s = MathF.Sqrt(1 + matrix.M22 - matrix.M11 - matrix.M33);
                float invS = 0.5f / s;
                q.X = (matrix.M21 + matrix.M12) * invS;
                q.Y = 0.5f * s;
                q.Z = (matrix.M32 + matrix.M23) * invS;
                q.W = (matrix.M31 - matrix.M13) * invS;
            }
            else
            {
                float s = MathF.Sqrt(1 + matrix.M33 - matrix.M11 - matrix.M22);
                float invS = 0.5f / s;
                q.X = (matrix.M31 + matrix.M13) * invS;
                q.Y = (matrix.M32 + matrix.M23) * invS;
                q.Z = 0.5f * s;
                q.W = (matrix.M12 - matrix.M21) * invS;
            }
            return q;
        }

        public readonly float LengthSquared() => X * X + Y * Y + Z * Z + W * W;
        public readonly float Length() => MathF.Sqrt(LengthSquared());

        public static float Dot(Quaternion quaternion1, Quaternion quaternion2) =>
            quaternion1.X * quaternion2.X + quaternion1.Y * quaternion2.Y +
            quaternion1.Z * quaternion2.Z + quaternion1.W * quaternion2.W;

        public static Quaternion Normalize(Quaternion value) => Divide(value, value.Length());
        public static Quaternion Conjugate(Quaternion value) => new Quaternion(-value.X, -value.Y, -value.Z, value.W);

        public static Quaternion Inverse(Quaternion value)
        {
            float invNorm = 1 / value.LengthSquared();
            return new Quaternion(-value.X * invNorm, -value.Y * invNorm, -value.Z * invNorm, value.W * invNorm);
        }

        // value1's rotation, then value2's.
        public static Quaternion Concatenate(Quaternion value1, Quaternion value2) => value2 * value1;

        // A straight line between the two, normalized; Slerp keeps a
        // constant angular speed instead.
        public static Quaternion Lerp(Quaternion quaternion1, Quaternion quaternion2, float amount)
        {
            float t1 = 1 - amount;
            Quaternion r = Dot(quaternion1, quaternion2) >= 0
                ? quaternion1 * t1 + quaternion2 * amount
                : quaternion1 * t1 - quaternion2 * amount;
            return Normalize(r);
        }

        public static Quaternion Slerp(Quaternion quaternion1, Quaternion quaternion2, float amount)
        {
            const float Epsilon = 1e-6f;
            float cosOmega = Dot(quaternion1, quaternion2);
            bool flip = false;
            if (cosOmega < 0)
            {
                flip = true;
                cosOmega = -cosOmega;
            }
            float s1, s2;
            if (cosOmega > 1 - Epsilon)
            {
                // Too close for sin(omega) to divide by: a straight line.
                s1 = 1 - amount;
                s2 = flip ? -amount : amount;
            }
            else
            {
                float omega = MathF.Acos(cosOmega);
                float invSinOmega = 1 / MathF.Sin(omega);
                s1 = MathF.Sin((1 - amount) * omega) * invSinOmega;
                s2 = MathF.Sin(amount * omega) * invSinOmega;
                if (flip)
                    s2 = -s2;
            }
            return quaternion1 * s1 + quaternion2 * s2;
        }

        public static Quaternion operator +(Quaternion value1, Quaternion value2) =>
            new Quaternion(value1.X + value2.X, value1.Y + value2.Y, value1.Z + value2.Z, value1.W + value2.W);

        public static Quaternion operator -(Quaternion value1, Quaternion value2) =>
            new Quaternion(value1.X - value2.X, value1.Y - value2.Y, value1.Z - value2.Z, value1.W - value2.W);

        public static Quaternion operator -(Quaternion value) => new Quaternion(-value.X, -value.Y, -value.Z, -value.W);

        public static Quaternion operator *(Quaternion value1, float value2) =>
            new Quaternion(value1.X * value2, value1.Y * value2, value1.Z * value2, value1.W * value2);

        // The Hamilton product: value2's rotation, then value1's.
        public static Quaternion operator *(Quaternion value1, Quaternion value2)
        {
            float q1x = value1.X, q1y = value1.Y, q1z = value1.Z, q1w = value1.W;
            float q2x = value2.X, q2y = value2.Y, q2z = value2.Z, q2w = value2.W;
            float cx = q1y * q2z - q1z * q2y;
            float cy = q1z * q2x - q1x * q2z;
            float cz = q1x * q2y - q1y * q2x;
            float dot = q1x * q2x + q1y * q2y + q1z * q2z;
            return new Quaternion(
                q1x * q2w + q2x * q1w + cx,
                q1y * q2w + q2y * q1w + cy,
                q1z * q2w + q2z * q1w + cz,
                q1w * q2w - dot);
        }

        public static Quaternion operator /(Quaternion value1, Quaternion value2) => value1 * Inverse(value2);

        public static bool operator ==(Quaternion value1, Quaternion value2) =>
            value1.X == value2.X && value1.Y == value2.Y && value1.Z == value2.Z && value1.W == value2.W;

        public static bool operator !=(Quaternion value1, Quaternion value2) => !(value1 == value2);

        public static Quaternion Add(Quaternion value1, Quaternion value2) => value1 + value2;
        public static Quaternion Subtract(Quaternion value1, Quaternion value2) => value1 - value2;
        public static Quaternion Multiply(Quaternion value1, Quaternion value2) => value1 * value2;
        public static Quaternion Multiply(Quaternion value1, float value2) => value1 * value2;
        public static Quaternion Divide(Quaternion value1, Quaternion value2) => value1 / value2;
        public static Quaternion Negate(Quaternion value) => -value;

        private static Quaternion Divide(Quaternion value, float divisor) =>
            new Quaternion(value.X / divisor, value.Y / divisor, value.Z / divisor, value.W / divisor);

        public readonly bool Equals(Quaternion other) =>
            X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

        public override readonly bool Equals(object obj) => obj is Quaternion other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z, W);

        // .NET's format: {X:0 Y:0 Z:0 W:1}.
        public override readonly string ToString() =>
            string.Concat(new string[]
            {
                "{X:", X.ToString(), " Y:", Y.ToString(), " Z:", Z.ToString(), " W:", W.ToString(), "}",
            });
    }
}

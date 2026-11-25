namespace System.Numerics
{
    // The plane of points p with Dot(Normal, p) + D == 0, with .NET's members.
    public struct Plane : IEquatable<Plane>
    {
        public Vector3 Normal;
        public float D;

        public Plane(float x, float y, float z, float d)
        {
            Normal = new Vector3(x, y, z);
            D = d;
        }

        public Plane(Vector3 normal, float d)
        {
            Normal = normal;
            D = d;
        }

        public Plane(Vector4 value)
        {
            Normal = new Vector3(value.X, value.Y, value.Z);
            D = value.W;
        }

        // The plane through three points; its normal follows the right-hand
        // rule from point1 to point2 to point3.
        public static Plane CreateFromVertices(Vector3 point1, Vector3 point2, Vector3 point3)
        {
            Vector3 normal = Vector3.Normalize(Vector3.Cross(point2 - point1, point3 - point1));
            return new Plane(normal, -Vector3.Dot(normal, point1));
        }

        public static float Dot(Plane plane, Vector4 value) =>
            plane.Normal.X * value.X + plane.Normal.Y * value.Y + plane.Normal.Z * value.Z + plane.D * value.W;

        // The signed distance of a point from the plane, when Normal has length 1.
        public static float DotCoordinate(Plane plane, Vector3 value) => Vector3.Dot(plane.Normal, value) + plane.D;

        public static float DotNormal(Plane plane, Vector3 value) => Vector3.Dot(plane.Normal, value);

        public static Plane Normalize(Plane value)
        {
            float lengthSquared = value.Normal.LengthSquared();
            if (MathF.Abs(lengthSquared - 1) < 1.192092896e-07f)
                return value;
            float inverse = 1 / MathF.Sqrt(lengthSquared);
            return new Plane(value.Normal * inverse, value.D * inverse);
        }

        // matrix must be invertible; Normal must have length 1.
        public static Plane Transform(Plane plane, Matrix4x4 matrix)
        {
            Matrix4x4.Invert(matrix, out Matrix4x4 m);
            float x = plane.Normal.X, y = plane.Normal.Y, z = plane.Normal.Z, w = plane.D;
            return new Plane(
                x * m.M11 + y * m.M12 + z * m.M13 + w * m.M14,
                x * m.M21 + y * m.M22 + z * m.M23 + w * m.M24,
                x * m.M31 + y * m.M32 + z * m.M33 + w * m.M34,
                x * m.M41 + y * m.M42 + z * m.M43 + w * m.M44);
        }

        public static Plane Transform(Plane plane, Quaternion rotation) =>
            new Plane(Vector3.Transform(plane.Normal, rotation), plane.D);

        public static bool operator ==(Plane value1, Plane value2) =>
            value1.Normal == value2.Normal && value1.D == value2.D;

        public static bool operator !=(Plane value1, Plane value2) => !(value1 == value2);

        public readonly bool Equals(Plane other) => Normal.Equals(other.Normal) && D.Equals(other.D);
        public override readonly bool Equals(object obj) => obj is Plane other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(Normal, D);

        // .NET's format: {Normal:<0, 1, 0> D:0}.
        public override readonly string ToString() =>
            string.Concat("{Normal:", Normal.ToString(), " D:", D.ToString()) + "}";
    }
}

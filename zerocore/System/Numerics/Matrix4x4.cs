using System.Runtime.CompilerServices;

namespace System.Numerics
{
    // A 3D transform with .NET's members. Row vectors, as in .NET: a point
    // is transformed as [x y z 1] * M, so M41..M43 are the translation, and
    // A * B applies A first. The projections are right-handed (the camera
    // looks down -Z), with depth mapped to 0..1, as in .NET and Direct3D.
    public struct Matrix4x4 : IEquatable<Matrix4x4>
    {
        public float M11, M12, M13, M14;
        public float M21, M22, M23, M24;
        public float M31, M32, M33, M34;
        public float M41, M42, M43, M44;

        public Matrix4x4(float m11, float m12, float m13, float m14,
                         float m21, float m22, float m23, float m24,
                         float m31, float m32, float m33, float m34,
                         float m41, float m42, float m43, float m44)
        {
            M11 = m11; M12 = m12; M13 = m13; M14 = m14;
            M21 = m21; M22 = m22; M23 = m23; M24 = m24;
            M31 = m31; M32 = m32; M33 = m33; M34 = m34;
            M41 = m41; M42 = m42; M43 = m43; M44 = m44;
        }

        // A 2D transform in the XY plane.
        public Matrix4x4(Matrix3x2 value)
        {
            M11 = value.M11; M12 = value.M12; M13 = 0; M14 = 0;
            M21 = value.M21; M22 = value.M22; M23 = 0; M24 = 0;
            M31 = 0; M32 = 0; M33 = 1; M34 = 0;
            M41 = value.M31; M42 = value.M32; M43 = 0; M44 = 1;
        }

        public static Matrix4x4 Identity => new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

        public readonly bool IsIdentity =>
            M11 == 1 && M12 == 0 && M13 == 0 && M14 == 0 &&
            M21 == 0 && M22 == 1 && M23 == 0 && M24 == 0 &&
            M31 == 0 && M32 == 0 && M33 == 1 && M34 == 0 &&
            M41 == 0 && M42 == 0 && M43 == 0 && M44 == 1;

        public Vector3 Translation
        {
            readonly get => new Vector3(M41, M42, M43);
            set
            {
                M41 = value.X;
                M42 = value.Y;
                M43 = value.Z;
            }
        }

        public float this[int row, int column]
        {
            readonly get
            {
                if ((uint)row >= 4 || (uint)column >= 4)
                    throw new ArgumentOutOfRangeException(nameof(row));
                Matrix4x4 copy = this;
                return Unsafe.Add(ref copy.M11, row * 4 + column);
            }
            set
            {
                if ((uint)row >= 4 || (uint)column >= 4)
                    throw new ArgumentOutOfRangeException(nameof(row));
                Unsafe.Add(ref M11, row * 4 + column) = value;
            }
        }

        public static Matrix4x4 CreateTranslation(Vector3 position) => CreateTranslation(position.X, position.Y, position.Z);
        public static Matrix4x4 CreateTranslation(float xPosition, float yPosition, float zPosition)
        {
            Matrix4x4 m = Identity;
            m.M41 = xPosition;
            m.M42 = yPosition;
            m.M43 = zPosition;
            return m;
        }

        public static Matrix4x4 CreateScale(float xScale, float yScale, float zScale) =>
            CreateScale(xScale, yScale, zScale, default);
        public static Matrix4x4 CreateScale(Vector3 scales) => CreateScale(scales.X, scales.Y, scales.Z, default);
        public static Matrix4x4 CreateScale(Vector3 scales, Vector3 centerPoint) =>
            CreateScale(scales.X, scales.Y, scales.Z, centerPoint);
        public static Matrix4x4 CreateScale(float scale) => CreateScale(scale, scale, scale, default);
        public static Matrix4x4 CreateScale(float scale, Vector3 centerPoint) => CreateScale(scale, scale, scale, centerPoint);

        // Scales about centerPoint, which stays where it is.
        public static Matrix4x4 CreateScale(float xScale, float yScale, float zScale, Vector3 centerPoint) =>
            new Matrix4x4(xScale, 0, 0, 0,
                          0, yScale, 0, 0,
                          0, 0, zScale, 0,
                          centerPoint.X * (1 - xScale), centerPoint.Y * (1 - yScale), centerPoint.Z * (1 - zScale), 1);

        public static Matrix4x4 CreateRotationX(float radians) => CreateRotationX(radians, default);
        public static Matrix4x4 CreateRotationX(float radians, Vector3 centerPoint)
        {
            float c = MathF.Cos(radians), s = MathF.Sin(radians);
            float y = centerPoint.Y * (1 - c) + centerPoint.Z * s;
            float z = centerPoint.Z * (1 - c) - centerPoint.Y * s;
            return new Matrix4x4(1, 0, 0, 0, 0, c, s, 0, 0, -s, c, 0, 0, y, z, 1);
        }

        public static Matrix4x4 CreateRotationY(float radians) => CreateRotationY(radians, default);
        public static Matrix4x4 CreateRotationY(float radians, Vector3 centerPoint)
        {
            float c = MathF.Cos(radians), s = MathF.Sin(radians);
            float x = centerPoint.X * (1 - c) - centerPoint.Z * s;
            float z = centerPoint.Z * (1 - c) + centerPoint.X * s;
            return new Matrix4x4(c, 0, -s, 0, 0, 1, 0, 0, s, 0, c, 0, x, 0, z, 1);
        }

        public static Matrix4x4 CreateRotationZ(float radians) => CreateRotationZ(radians, default);
        public static Matrix4x4 CreateRotationZ(float radians, Vector3 centerPoint)
        {
            float c = MathF.Cos(radians), s = MathF.Sin(radians);
            float x = centerPoint.X * (1 - c) + centerPoint.Y * s;
            float y = centerPoint.Y * (1 - c) - centerPoint.X * s;
            return new Matrix4x4(c, s, 0, 0, -s, c, 0, 0, 0, 0, 1, 0, x, y, 0, 1);
        }

        // axis must be of length 1.
        public static Matrix4x4 CreateFromAxisAngle(Vector3 axis, float angle)
        {
            float x = axis.X, y = axis.Y, z = axis.Z;
            float sa = MathF.Sin(angle), ca = MathF.Cos(angle);
            float xx = x * x, yy = y * y, zz = z * z;
            float xy = x * y, xz = x * z, yz = y * z;
            return new Matrix4x4(
                xx + ca * (1 - xx), xy - ca * xy + sa * z, xz - ca * xz - sa * y, 0,
                xy - ca * xy - sa * z, yy + ca * (1 - yy), yz - ca * yz + sa * x, 0,
                xz - ca * xz + sa * y, yz - ca * yz - sa * x, zz + ca * (1 - zz), 0,
                0, 0, 0, 1);
        }

        public static Matrix4x4 CreateFromQuaternion(Quaternion quaternion)
        {
            float xx = quaternion.X * quaternion.X;
            float yy = quaternion.Y * quaternion.Y;
            float zz = quaternion.Z * quaternion.Z;
            float xy = quaternion.X * quaternion.Y;
            float wz = quaternion.Z * quaternion.W;
            float xz = quaternion.Z * quaternion.X;
            float wy = quaternion.Y * quaternion.W;
            float yz = quaternion.Y * quaternion.Z;
            float wx = quaternion.X * quaternion.W;
            return new Matrix4x4(
                1 - 2 * (yy + zz), 2 * (xy + wz), 2 * (xz - wy), 0,
                2 * (xy - wz), 1 - 2 * (zz + xx), 2 * (yz + wx), 0,
                2 * (xz + wy), 2 * (yz - wx), 1 - 2 * (yy + xx), 0,
                0, 0, 0, 1);
        }

        public static Matrix4x4 CreateFromYawPitchRoll(float yaw, float pitch, float roll) =>
            CreateFromQuaternion(Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll));

        // A view matrix: world space to the camera's, the camera looking at
        // cameraTarget with cameraUpVector roughly up.
        public static Matrix4x4 CreateLookAt(Vector3 cameraPosition, Vector3 cameraTarget, Vector3 cameraUpVector)
        {
            Vector3 zaxis = Vector3.Normalize(cameraPosition - cameraTarget);
            Vector3 xaxis = Vector3.Normalize(Vector3.Cross(cameraUpVector, zaxis));
            Vector3 yaxis = Vector3.Cross(zaxis, xaxis);
            return new Matrix4x4(
                xaxis.X, yaxis.X, zaxis.X, 0,
                xaxis.Y, yaxis.Y, zaxis.Y, 0,
                xaxis.Z, yaxis.Z, zaxis.Z, 0,
                -Vector3.Dot(xaxis, cameraPosition), -Vector3.Dot(yaxis, cameraPosition), -Vector3.Dot(zaxis, cameraPosition), 1);
        }

        // The opposite of a view matrix: an object at position, facing forward.
        public static Matrix4x4 CreateWorld(Vector3 position, Vector3 forward, Vector3 up)
        {
            Vector3 zaxis = Vector3.Normalize(-forward);
            Vector3 xaxis = Vector3.Normalize(Vector3.Cross(up, zaxis));
            Vector3 yaxis = Vector3.Cross(zaxis, xaxis);
            return new Matrix4x4(
                xaxis.X, xaxis.Y, xaxis.Z, 0,
                yaxis.X, yaxis.Y, yaxis.Z, 0,
                zaxis.X, zaxis.Y, zaxis.Z, 0,
                position.X, position.Y, position.Z, 1);
        }

        // Rotates an object about its position so that it faces the camera.
        public static Matrix4x4 CreateBillboard(Vector3 objectPosition, Vector3 cameraPosition,
                                                Vector3 cameraUpVector, Vector3 cameraForwardVector)
        {
            Vector3 zaxis = objectPosition - cameraPosition;
            float norm = zaxis.LengthSquared();
            zaxis = norm < 1e-4f ? -cameraForwardVector : zaxis * (1 / MathF.Sqrt(norm));
            Vector3 xaxis = Vector3.Normalize(Vector3.Cross(cameraUpVector, zaxis));
            Vector3 yaxis = Vector3.Cross(zaxis, xaxis);
            return new Matrix4x4(
                xaxis.X, xaxis.Y, xaxis.Z, 0,
                yaxis.X, yaxis.Y, yaxis.Z, 0,
                zaxis.X, zaxis.Y, zaxis.Z, 0,
                objectPosition.X, objectPosition.Y, objectPosition.Z, 1);
        }

        public static Matrix4x4 CreateOrthographic(float width, float height, float zNearPlane, float zFarPlane)
        {
            float range = 1 / (zNearPlane - zFarPlane);
            return new Matrix4x4(
                2 / width, 0, 0, 0,
                0, 2 / height, 0, 0,
                0, 0, range, 0,
                0, 0, range * zNearPlane, 1);
        }

        public static Matrix4x4 CreateOrthographicOffCenter(float left, float right, float bottom, float top,
                                                            float zNearPlane, float zFarPlane)
        {
            float range = 1 / (zNearPlane - zFarPlane);
            return new Matrix4x4(
                2 / (right - left), 0, 0, 0,
                0, 2 / (top - bottom), 0, 0,
                0, 0, range, 0,
                (left + right) / (left - right), (top + bottom) / (bottom - top), range * zNearPlane, 1);
        }

        public static Matrix4x4 CreatePerspective(float width, float height, float nearPlaneDistance, float farPlaneDistance)
        {
            CheckPlanes(nearPlaneDistance, farPlaneDistance);
            float range = Range(nearPlaneDistance, farPlaneDistance);
            return new Matrix4x4(
                2 * nearPlaneDistance / width, 0, 0, 0,
                0, 2 * nearPlaneDistance / height, 0, 0,
                0, 0, range, -1,
                0, 0, range * nearPlaneDistance, 0);
        }

        public static Matrix4x4 CreatePerspectiveFieldOfView(float fieldOfView, float aspectRatio,
                                                             float nearPlaneDistance, float farPlaneDistance)
        {
            if (fieldOfView <= 0 || fieldOfView >= MathF.PI)
                throw new ArgumentOutOfRangeException(nameof(fieldOfView));
            CheckPlanes(nearPlaneDistance, farPlaneDistance);
            float yScale = 1 / MathF.Tan(fieldOfView * 0.5f);
            float xScale = yScale / aspectRatio;
            float range = Range(nearPlaneDistance, farPlaneDistance);
            return new Matrix4x4(
                xScale, 0, 0, 0,
                0, yScale, 0, 0,
                0, 0, range, -1,
                0, 0, range * nearPlaneDistance, 0);
        }

        public static Matrix4x4 CreatePerspectiveOffCenter(float left, float right, float bottom, float top,
                                                           float nearPlaneDistance, float farPlaneDistance)
        {
            CheckPlanes(nearPlaneDistance, farPlaneDistance);
            float range = Range(nearPlaneDistance, farPlaneDistance);
            return new Matrix4x4(
                2 * nearPlaneDistance / (right - left), 0, 0, 0,
                0, 2 * nearPlaneDistance / (top - bottom), 0, 0,
                (left + right) / (right - left), (top + bottom) / (top - bottom), range, -1,
                0, 0, range * nearPlaneDistance, 0);
        }

        private static void CheckPlanes(float near, float far)
        {
            if (near <= 0)
                throw new ArgumentOutOfRangeException("nearPlaneDistance");
            if (far <= 0)
                throw new ArgumentOutOfRangeException("farPlaneDistance");
            if (near >= far)
                throw new ArgumentOutOfRangeException("nearPlaneDistance");
        }

        // An infinitely far plane is allowed.
        private static float Range(float near, float far) =>
            float.IsPositiveInfinity(far) ? -1 : far / (near - far);

        // A mirror image through the plane.
        public static Matrix4x4 CreateReflection(Plane value)
        {
            value = Plane.Normalize(value);
            float a = value.Normal.X, b = value.Normal.Y, c = value.Normal.Z;
            float fa = -2 * a, fb = -2 * b, fc = -2 * c;
            return new Matrix4x4(
                fa * a + 1, fb * a, fc * a, 0,
                fa * b, fb * b + 1, fc * b, 0,
                fa * c, fb * c, fc * c + 1, 0,
                fa * value.D, fb * value.D, fc * value.D, 1);
        }

        // Flattens geometry onto the plane, as a light shining along
        // lightDirection would cast its shadow (W = 0) or from a point (W = 1).
        public static Matrix4x4 CreateShadow(Vector3 lightDirection, Plane plane)
        {
            Plane p = Plane.Normalize(plane);
            float dot = p.Normal.X * lightDirection.X + p.Normal.Y * lightDirection.Y + p.Normal.Z * lightDirection.Z;
            float a = -p.Normal.X, b = -p.Normal.Y, c = -p.Normal.Z, d = -p.D;
            return new Matrix4x4(
                a * lightDirection.X + dot, a * lightDirection.Y, a * lightDirection.Z, 0,
                b * lightDirection.X, b * lightDirection.Y + dot, b * lightDirection.Z, 0,
                c * lightDirection.X, c * lightDirection.Y, c * lightDirection.Z + dot, 0,
                d * lightDirection.X, d * lightDirection.Y, d * lightDirection.Z, dot);
        }

        public readonly float GetDeterminant()
        {
            float a = M11, b = M12, c = M13, d = M14;
            float e = M21, f = M22, g = M23, h = M24;
            float i = M31, j = M32, k = M33, l = M34;
            float m = M41, n = M42, o = M43, p = M44;
            float kp_lo = k * p - l * o;
            float jp_ln = j * p - l * n;
            float jo_kn = j * o - k * n;
            float ip_lm = i * p - l * m;
            float io_km = i * o - k * m;
            float in_jm = i * n - j * m;
            return a * (f * kp_lo - g * jp_ln + h * jo_kn) -
                   b * (e * kp_lo - g * ip_lm + h * io_km) +
                   c * (e * jp_ln - f * ip_lm + h * in_jm) -
                   d * (e * jo_kn - f * io_km + g * in_jm);
        }

        // By cofactors. False, with every element NaN, when the matrix has
        // no inverse.
        public static bool Invert(Matrix4x4 matrix, out Matrix4x4 result)
        {
            float a = matrix.M11, b = matrix.M12, c = matrix.M13, d = matrix.M14;
            float e = matrix.M21, f = matrix.M22, g = matrix.M23, h = matrix.M24;
            float i = matrix.M31, j = matrix.M32, k = matrix.M33, l = matrix.M34;
            float m = matrix.M41, n = matrix.M42, o = matrix.M43, p = matrix.M44;

            float kp_lo = k * p - l * o;
            float jp_ln = j * p - l * n;
            float jo_kn = j * o - k * n;
            float ip_lm = i * p - l * m;
            float io_km = i * o - k * m;
            float in_jm = i * n - j * m;

            float a11 = +(f * kp_lo - g * jp_ln + h * jo_kn);
            float a12 = -(e * kp_lo - g * ip_lm + h * io_km);
            float a13 = +(e * jp_ln - f * ip_lm + h * in_jm);
            float a14 = -(e * jo_kn - f * io_km + g * in_jm);

            float det = a * a11 + b * a12 + c * a13 + d * a14;
            if (MathF.Abs(det) < float.Epsilon)
            {
                float nan = float.NaN;
                result = new Matrix4x4(nan, nan, nan, nan, nan, nan, nan, nan, nan, nan, nan, nan, nan, nan, nan, nan);
                return false;
            }
            float invDet = 1 / det;

            float gp_ho = g * p - h * o;
            float fp_hn = f * p - h * n;
            float fo_gn = f * o - g * n;
            float ep_hm = e * p - h * m;
            float eo_gm = e * o - g * m;
            float en_fm = e * n - f * m;

            float gl_hk = g * l - h * k;
            float fl_hj = f * l - h * j;
            float fk_gj = f * k - g * j;
            float el_hi = e * l - h * i;
            float ek_gi = e * k - g * i;
            float ej_fi = e * j - f * i;

            result = new Matrix4x4(
                a11 * invDet,
                -(b * kp_lo - c * jp_ln + d * jo_kn) * invDet,
                +(b * gp_ho - c * fp_hn + d * fo_gn) * invDet,
                -(b * gl_hk - c * fl_hj + d * fk_gj) * invDet,

                a12 * invDet,
                +(a * kp_lo - c * ip_lm + d * io_km) * invDet,
                -(a * gp_ho - c * ep_hm + d * eo_gm) * invDet,
                +(a * gl_hk - c * el_hi + d * ek_gi) * invDet,

                a13 * invDet,
                -(a * jp_ln - b * ip_lm + d * in_jm) * invDet,
                +(a * fp_hn - b * ep_hm + d * en_fm) * invDet,
                -(a * fl_hj - b * el_hi + d * ej_fi) * invDet,

                a14 * invDet,
                +(a * jo_kn - b * io_km + c * in_jm) * invDet,
                -(a * fo_gn - b * eo_gm + c * en_fm) * invDet,
                +(a * fk_gj - b * ek_gi + c * ej_fi) * invDet);
            return true;
        }

        // Splits an affine matrix into scale, then rotation, then translation,
        // by .NET's method: the rows, longest first, are made unit length; a
        // row of (nearly) zero length is replaced by one at right angles to
        // the others; a mirror image puts the negative scale on the longest.
        // False, with rotation Identity, when the rows aren't at right angles.
        public static bool Decompose(Matrix4x4 matrix, out Vector3 scale, out Quaternion rotation, out Vector3 translation)
        {
            const float Epsilon = 0.0001f;
            translation = new Vector3(matrix.M41, matrix.M42, matrix.M43);

            Span<Vector3> rows = stackalloc Vector3[3];
            rows[0] = new Vector3(matrix.M11, matrix.M12, matrix.M13);
            rows[1] = new Vector3(matrix.M21, matrix.M22, matrix.M23);
            rows[2] = new Vector3(matrix.M31, matrix.M32, matrix.M33);
            Span<float> scales = stackalloc float[3];
            scales[0] = rows[0].Length();
            scales[1] = rows[1].Length();
            scales[2] = rows[2].Length();

            // a, b, c: the rows from longest to shortest.
            int a, b, c;
            float x = scales[0], y = scales[1], z = scales[2];
            if (x < y)
            {
                if (y < z) { a = 2; b = 1; c = 0; }
                else
                {
                    a = 1;
                    if (x < z) { b = 2; c = 0; } else { b = 0; c = 2; }
                }
            }
            else
            {
                if (x < z) { a = 2; b = 0; c = 1; }
                else
                {
                    a = 0;
                    if (y < z) { b = 2; c = 1; } else { b = 1; c = 2; }
                }
            }

            if (scales[a] < Epsilon)
                rows[a] = Basis(a);
            rows[a] = Vector3.Normalize(rows[a]);

            if (scales[b] < Epsilon)
            {
                // At right angles to rows[a]: its cross product with the
                // axis it has least of.
                float ax = MathF.Abs(rows[a].X), ay = MathF.Abs(rows[a].Y), az = MathF.Abs(rows[a].Z);
                int least;
                if (ax < ay)
                    least = ay < az ? 0 : ax < az ? 0 : 2;
                else
                    least = ax < az ? 1 : ay < az ? 1 : 2;
                rows[b] = Vector3.Cross(rows[a], Basis(least));
            }
            rows[b] = Vector3.Normalize(rows[b]);

            if (scales[c] < Epsilon)
                rows[c] = Vector3.Cross(rows[a], rows[b]);
            rows[c] = Vector3.Normalize(rows[c]);

            Matrix4x4 r = new Matrix4x4(
                rows[0].X, rows[0].Y, rows[0].Z, 0,
                rows[1].X, rows[1].Y, rows[1].Z, 0,
                rows[2].X, rows[2].Y, rows[2].Z, 0,
                0, 0, 0, 1);
            float det = r.GetDeterminant();
            if (det < 0)
            {
                scales[a] = -scales[a];
                rows[a] = -rows[a];
                det = -det;
                r = new Matrix4x4(
                    rows[0].X, rows[0].Y, rows[0].Z, 0,
                    rows[1].X, rows[1].Y, rows[1].Z, 0,
                    rows[2].X, rows[2].Y, rows[2].Z, 0,
                    0, 0, 0, 1);
            }
            scale = new Vector3(scales[0], scales[1], scales[2]);

            det -= 1;
            det *= det;
            if (Epsilon < det)
            {
                rotation = Quaternion.Identity;
                return false;
            }
            rotation = Quaternion.CreateFromRotationMatrix(r);
            return true;
        }

        private static Vector3 Basis(int axis) => axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;

        // value's transform, then the rotation.
        public static Matrix4x4 Transform(Matrix4x4 value, Quaternion rotation) => value * CreateFromQuaternion(rotation);

        public static Matrix4x4 Transpose(Matrix4x4 matrix) =>
            new Matrix4x4(
                matrix.M11, matrix.M21, matrix.M31, matrix.M41,
                matrix.M12, matrix.M22, matrix.M32, matrix.M42,
                matrix.M13, matrix.M23, matrix.M33, matrix.M43,
                matrix.M14, matrix.M24, matrix.M34, matrix.M44);

        public static Matrix4x4 Lerp(Matrix4x4 matrix1, Matrix4x4 matrix2, float amount) =>
            matrix1 + (matrix2 - matrix1) * amount;

        public static Matrix4x4 operator +(Matrix4x4 value1, Matrix4x4 value2) =>
            new Matrix4x4(
                value1.M11 + value2.M11, value1.M12 + value2.M12, value1.M13 + value2.M13, value1.M14 + value2.M14,
                value1.M21 + value2.M21, value1.M22 + value2.M22, value1.M23 + value2.M23, value1.M24 + value2.M24,
                value1.M31 + value2.M31, value1.M32 + value2.M32, value1.M33 + value2.M33, value1.M34 + value2.M34,
                value1.M41 + value2.M41, value1.M42 + value2.M42, value1.M43 + value2.M43, value1.M44 + value2.M44);

        public static Matrix4x4 operator -(Matrix4x4 value1, Matrix4x4 value2) =>
            new Matrix4x4(
                value1.M11 - value2.M11, value1.M12 - value2.M12, value1.M13 - value2.M13, value1.M14 - value2.M14,
                value1.M21 - value2.M21, value1.M22 - value2.M22, value1.M23 - value2.M23, value1.M24 - value2.M24,
                value1.M31 - value2.M31, value1.M32 - value2.M32, value1.M33 - value2.M33, value1.M34 - value2.M34,
                value1.M41 - value2.M41, value1.M42 - value2.M42, value1.M43 - value2.M43, value1.M44 - value2.M44);

        public static Matrix4x4 operator -(Matrix4x4 value) => value * -1;

        public static Matrix4x4 operator *(Matrix4x4 value1, float value2) =>
            new Matrix4x4(
                value1.M11 * value2, value1.M12 * value2, value1.M13 * value2, value1.M14 * value2,
                value1.M21 * value2, value1.M22 * value2, value1.M23 * value2, value1.M24 * value2,
                value1.M31 * value2, value1.M32 * value2, value1.M33 * value2, value1.M34 * value2,
                value1.M41 * value2, value1.M42 * value2, value1.M43 * value2, value1.M44 * value2);

        // value1 first, then value2.
        public static Matrix4x4 operator *(Matrix4x4 value1, Matrix4x4 value2)
        {
            Matrix4x4 r;
            r.M11 = value1.M11 * value2.M11 + value1.M12 * value2.M21 + value1.M13 * value2.M31 + value1.M14 * value2.M41;
            r.M12 = value1.M11 * value2.M12 + value1.M12 * value2.M22 + value1.M13 * value2.M32 + value1.M14 * value2.M42;
            r.M13 = value1.M11 * value2.M13 + value1.M12 * value2.M23 + value1.M13 * value2.M33 + value1.M14 * value2.M43;
            r.M14 = value1.M11 * value2.M14 + value1.M12 * value2.M24 + value1.M13 * value2.M34 + value1.M14 * value2.M44;

            r.M21 = value1.M21 * value2.M11 + value1.M22 * value2.M21 + value1.M23 * value2.M31 + value1.M24 * value2.M41;
            r.M22 = value1.M21 * value2.M12 + value1.M22 * value2.M22 + value1.M23 * value2.M32 + value1.M24 * value2.M42;
            r.M23 = value1.M21 * value2.M13 + value1.M22 * value2.M23 + value1.M23 * value2.M33 + value1.M24 * value2.M43;
            r.M24 = value1.M21 * value2.M14 + value1.M22 * value2.M24 + value1.M23 * value2.M34 + value1.M24 * value2.M44;

            r.M31 = value1.M31 * value2.M11 + value1.M32 * value2.M21 + value1.M33 * value2.M31 + value1.M34 * value2.M41;
            r.M32 = value1.M31 * value2.M12 + value1.M32 * value2.M22 + value1.M33 * value2.M32 + value1.M34 * value2.M42;
            r.M33 = value1.M31 * value2.M13 + value1.M32 * value2.M23 + value1.M33 * value2.M33 + value1.M34 * value2.M43;
            r.M34 = value1.M31 * value2.M14 + value1.M32 * value2.M24 + value1.M33 * value2.M34 + value1.M34 * value2.M44;

            r.M41 = value1.M41 * value2.M11 + value1.M42 * value2.M21 + value1.M43 * value2.M31 + value1.M44 * value2.M41;
            r.M42 = value1.M41 * value2.M12 + value1.M42 * value2.M22 + value1.M43 * value2.M32 + value1.M44 * value2.M42;
            r.M43 = value1.M41 * value2.M13 + value1.M42 * value2.M23 + value1.M43 * value2.M33 + value1.M44 * value2.M43;
            r.M44 = value1.M41 * value2.M14 + value1.M42 * value2.M24 + value1.M43 * value2.M34 + value1.M44 * value2.M44;
            return r;
        }

        public static bool operator ==(Matrix4x4 value1, Matrix4x4 value2) =>
            value1.M11 == value2.M11 && value1.M12 == value2.M12 && value1.M13 == value2.M13 && value1.M14 == value2.M14 &&
            value1.M21 == value2.M21 && value1.M22 == value2.M22 && value1.M23 == value2.M23 && value1.M24 == value2.M24 &&
            value1.M31 == value2.M31 && value1.M32 == value2.M32 && value1.M33 == value2.M33 && value1.M34 == value2.M34 &&
            value1.M41 == value2.M41 && value1.M42 == value2.M42 && value1.M43 == value2.M43 && value1.M44 == value2.M44;

        public static bool operator !=(Matrix4x4 value1, Matrix4x4 value2) => !(value1 == value2);

        public static Matrix4x4 Add(Matrix4x4 value1, Matrix4x4 value2) => value1 + value2;
        public static Matrix4x4 Subtract(Matrix4x4 value1, Matrix4x4 value2) => value1 - value2;
        public static Matrix4x4 Multiply(Matrix4x4 value1, Matrix4x4 value2) => value1 * value2;
        public static Matrix4x4 Multiply(Matrix4x4 value1, float value2) => value1 * value2;
        public static Matrix4x4 Negate(Matrix4x4 value) => -value;

        public readonly bool Equals(Matrix4x4 other) =>
            M11.Equals(other.M11) && M12.Equals(other.M12) && M13.Equals(other.M13) && M14.Equals(other.M14) &&
            M21.Equals(other.M21) && M22.Equals(other.M22) && M23.Equals(other.M23) && M24.Equals(other.M24) &&
            M31.Equals(other.M31) && M32.Equals(other.M32) && M33.Equals(other.M33) && M34.Equals(other.M34) &&
            M41.Equals(other.M41) && M42.Equals(other.M42) && M43.Equals(other.M43) && M44.Equals(other.M44);

        public override readonly bool Equals(object obj) => obj is Matrix4x4 other && Equals(other);

        public override readonly int GetHashCode()
        {
            HashCode hash = default;
            hash.Add(M11); hash.Add(M12); hash.Add(M13); hash.Add(M14);
            hash.Add(M21); hash.Add(M22); hash.Add(M23); hash.Add(M24);
            hash.Add(M31); hash.Add(M32); hash.Add(M33); hash.Add(M34);
            hash.Add(M41); hash.Add(M42); hash.Add(M43); hash.Add(M44);
            return hash.ToHashCode();
        }

        // .NET's format: { {M11:1 M12:0 M13:0 M14:0} {M21:0 ...} ... }.
        public override readonly string ToString() =>
            string.Concat(new string[]
            {
                "{ {M11:", M11.ToString(), " M12:", M12.ToString(), " M13:", M13.ToString(), " M14:", M14.ToString(),
                "} {M21:", M21.ToString(), " M22:", M22.ToString(), " M23:", M23.ToString(), " M24:", M24.ToString(),
                "} {M31:", M31.ToString(), " M32:", M32.ToString(), " M33:", M33.ToString(), " M34:", M34.ToString(),
                "} {M41:", M41.ToString(), " M42:", M42.ToString(), " M43:", M43.ToString(), " M44:", M44.ToString(),
                "} }",
            });
    }
}

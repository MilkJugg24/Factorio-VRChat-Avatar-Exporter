using System;
using System.Globalization;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// A double precision unit quaternion for rotations in Core's right-handed spaces
    /// (positive angles turn counterclockwise when looking down the axis, the right-hand rule,
    /// matching <c>rot()</c> in tools/dragon_render/model.py).
    /// <c>(a * b).Rotate(v) == a.Rotate(b.Rotate(v))</c>.
    /// </summary>
    public readonly struct Quat : IEquatable<Quat>
    {
        public readonly double W;
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Quat(double w, double x, double y, double z)
        {
            W = w;
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Quat Identity = new Quat(1, 0, 0, 0);

        /// <summary>Rotation by <paramref name="angle"/> radians about <paramref name="axis"/> (normalized here).</summary>
        public static Quat FromAxisAngle(Vec3 axis, double angle)
        {
            Vec3 a = axis.Normalized;
            double h = angle * 0.5;
            double s = Math.Sin(h);
            return new Quat(Math.Cos(h), a.X * s, a.Y * s, a.Z * s);
        }

        /// <summary>
        /// The rotation that maps the unit axes X, Y, Z to <paramref name="right"/>,
        /// <paramref name="forward"/> and <paramref name="up"/>. The basis is made orthonormal first,
        /// keeping <paramref name="forward"/>'s direction and the plane of forward and up.
        /// </summary>
        public static Quat FromBasis(Vec3 right, Vec3 forward, Vec3 up)
        {
            Vec3 f = forward.Normalized;
            Vec3 r = Vec3.Cross(f, up).Normalized;
            if (r.LengthSquared < 0.5)
            {
                // up parallel to forward: fall back on the given right vector
                r = (right - f * Vec3.Dot(right, f)).Normalized;
            }
            Vec3 u = Vec3.Cross(r, f);
            return FromMatrix(r, f, u);
        }

        /// <summary>Quaternion of the rotation matrix whose columns are c0, c1, c2 (orthonormal, det +1).</summary>
        public static Quat FromMatrix(Vec3 c0, Vec3 c1, Vec3 c2)
        {
            double m00 = c0.X, m10 = c0.Y, m20 = c0.Z;
            double m01 = c1.X, m11 = c1.Y, m21 = c1.Z;
            double m02 = c2.X, m12 = c2.Y, m22 = c2.Z;
            double trace = m00 + m11 + m22;
            double w, x, y, z;
            if (trace > 0)
            {
                double s = Math.Sqrt(trace + 1.0) * 2;
                w = 0.25 * s;
                x = (m21 - m12) / s;
                y = (m02 - m20) / s;
                z = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                double s = Math.Sqrt(1.0 + m00 - m11 - m22) * 2;
                w = (m21 - m12) / s;
                x = 0.25 * s;
                y = (m01 + m10) / s;
                z = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                double s = Math.Sqrt(1.0 + m11 - m00 - m22) * 2;
                w = (m02 - m20) / s;
                x = (m01 + m10) / s;
                y = 0.25 * s;
                z = (m12 + m21) / s;
            }
            else
            {
                double s = Math.Sqrt(1.0 + m22 - m00 - m11) * 2;
                w = (m10 - m01) / s;
                x = (m02 + m20) / s;
                y = (m12 + m21) / s;
                z = 0.25 * s;
            }
            return new Quat(w, x, y, z).Normalized;
        }

        /// <summary>The smallest rotation that turns direction <paramref name="from"/> into <paramref name="to"/>.</summary>
        public static Quat FromTo(Vec3 from, Vec3 to)
        {
            Vec3 a = from.Normalized;
            Vec3 b = to.Normalized;
            double d = Vec3.Dot(a, b);
            if (d > 1 - 1e-12)
            {
                return Identity;
            }
            if (d < -1 + 1e-12)
            {
                // opposite: turn half way round any axis perpendicular to a
                Vec3 axis = Vec3.Cross(a, Vec3.UnitX);
                if (axis.LengthSquared < 1e-12)
                {
                    axis = Vec3.Cross(a, Vec3.UnitY);
                }
                return FromAxisAngle(axis, Math.PI);
            }
            Vec3 c = Vec3.Cross(a, b);
            return new Quat(1 + d, c.X, c.Y, c.Z).Normalized;
        }

        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);

        public Quat Conjugate => new Quat(W, -X, -Y, -Z);

        /// <summary>The inverse of a unit quaternion.</summary>
        public Quat Inverse => Conjugate;

        public double Norm => Math.Sqrt(W * W + X * X + Y * Y + Z * Z);

        public Quat Normalized
        {
            get
            {
                double n = Norm;
                return n > 1e-15 ? new Quat(W / n, X / n, Y / n, Z / n) : Identity;
            }
        }

        public Vec3 Rotate(Vec3 v)
        {
            // v + 2w(q x v) + 2 q x (q x v)
            var q = new Vec3(X, Y, Z);
            Vec3 t = Vec3.Cross(q, v) * 2;
            return v + t * W + Vec3.Cross(q, t);
        }

        public Vec3 Right => Rotate(Vec3.UnitX);
        public Vec3 Forward => Rotate(Vec3.UnitY);
        public Vec3 Up => Rotate(Vec3.UnitZ);

        public static double Dot(Quat a, Quat b) => a.W * b.W + a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>The angle in radians of the rotation from <paramref name="a"/> to <paramref name="b"/>.</summary>
        public static double Angle(Quat a, Quat b)
        {
            double d = Math.Min(1.0, Math.Abs(Dot(a.Normalized, b.Normalized)));
            return 2 * Math.Acos(d);
        }

        /// <summary>Spherical interpolation along the shorter arc.</summary>
        public static Quat Slerp(Quat a, Quat b, double t)
        {
            double d = Dot(a, b);
            if (d < 0)
            {
                b = new Quat(-b.W, -b.X, -b.Y, -b.Z);
                d = -d;
            }
            if (d > 0.9995)
            {
                return new Quat(a.W + (b.W - a.W) * t, a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t,
                    a.Z + (b.Z - a.Z) * t).Normalized;
            }
            double theta = Math.Acos(d);
            double s = Math.Sin(theta);
            double wa = Math.Sin((1 - t) * theta) / s;
            double wb = Math.Sin(t * theta) / s;
            return new Quat(a.W * wa + b.W * wb, a.X * wa + b.X * wb, a.Y * wa + b.Y * wb, a.Z * wa + b.Z * wb);
        }

        /// <summary>True when both describe the same rotation within <paramref name="toleranceRadians"/>.</summary>
        public bool ApproximatelyEquals(Quat other, double toleranceRadians) => Angle(this, other) <= toleranceRadians;

        public bool Equals(Quat other) => W.Equals(other.W) && X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is Quat other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(W, X, Y, Z);

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "(w {0:R}, x {1:R}, y {2:R}, z {3:R})", W, X, Y, Z);
    }
}

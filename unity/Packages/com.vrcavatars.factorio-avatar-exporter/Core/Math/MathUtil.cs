using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>Literal ports of the helpers in tools/dragon_render/model.py.</summary>
    public static class MathUtil
    {
        public const double Tau = 2.0 * Math.PI;

        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        public static double Radians(double degrees) => degrees * Math.PI / 180.0;

        /// <summary><c>smoothstep(e0, e1, x)</c> from model.py.</summary>
        public static double Smoothstep(double e0, double e1, double x)
        {
            double t = Clamp((x - e0) / (e1 - e0), 0.0, 1.0);
            return t * t * (3 - 2 * t);
        }

        /// <summary><c>rot(v, axis, ang)</c>: Rodrigues rotation of v about axis (normalized as model.py's norm()).</summary>
        public static Vec3 Rot(Vec3 v, Vec3 axis, double angle)
        {
            Vec3 a = axis.Normalized;
            double c = Math.Cos(angle);
            double s = Math.Sin(angle);
            return v * c + Vec3.Cross(a, v) * s + a * Vec3.Dot(a, v) * (1 - c);
        }

        /// <summary><c>rot_about(p, pivot, axis, ang)</c>.</summary>
        public static Vec3 RotAbout(Vec3 p, Vec3 pivot, Vec3 axis, double angle) => pivot + Rot(p - pivot, axis, angle);

        /// <summary>Python's <c>x % m</c> for a positive modulus (the result has the sign of m).</summary>
        public static double PositiveModulo(double x, double m)
        {
            double r = x % m;
            return r < 0 ? r + m : r;
        }
    }
}

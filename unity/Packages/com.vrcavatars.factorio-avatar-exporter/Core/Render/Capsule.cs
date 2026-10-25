using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// A capsule of the stick figure: every point within <see cref="Radius"/> of the segment from
    /// <see cref="A"/> to <see cref="B"/> (a sphere when they coincide). World space, tiles.
    /// </summary>
    public readonly struct Capsule
    {
        // segments shorter than this are treated as spheres
        private const double MinLength = 1e-12;

        private readonly Vec3 axis;
        private readonly double length;

        /// <summary>The capsule around the segment <paramref name="a"/> - <paramref name="b"/> (world space, tiles).</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is not a positive finite number.</exception>
        /// <exception cref="ArgumentException">An end point is not finite.</exception>
        public Capsule(Vec3 a, Vec3 b, double radius, StickPart part)
        {
            if (!(radius > 0) || !VecChecks.IsFinite(radius))
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "capsule radius must be positive and finite");
            }
            if (!VecChecks.IsFinite(a) || !VecChecks.IsFinite(b))
            {
                throw new ArgumentException("capsule end points must be finite, got " + a + " and " + b,
                    VecChecks.IsFinite(a) ? nameof(b) : nameof(a));
            }
            A = a;
            B = b;
            Radius = radius;
            Part = part;
            length = (b - a).Length;
            axis = length > MinLength ? (b - a) / length : Vec3.Zero;
        }

        /// <summary>One end of the axis segment, tiles.</summary>
        public Vec3 A { get; }

        /// <summary>The other end of the axis segment, tiles.</summary>
        public Vec3 B { get; }

        /// <summary>Distance of the surface from the axis segment, tiles.</summary>
        public double Radius { get; }

        /// <summary>The part of the figure the capsule belongs to, which sets its color.</summary>
        public StickPart Part { get; }

        /// <summary>The smallest axis-aligned box holding the capsule.</summary>
        public Bounds3 Bounds => new Bounds3(Vec3.Min(A, B), Vec3.Max(A, B)).Expanded(Radius);

        /// <summary>The same capsule moved by <paramref name="offset"/>.</summary>
        public Capsule Translated(Vec3 offset) => new Capsule(A + offset, B + offset, Radius, Part);

        /// <summary>
        /// Signed distance from <paramref name="point"/> to the surface: negative inside, zero on it.
        /// </summary>
        public double Distance(Vec3 point) => Vec3.Distance(point, ClosestAxisPoint(point)) - Radius;

        /// <summary>The outward unit normal of the surface at (or nearest to) <paramref name="surfacePoint"/>.</summary>
        public Vec3 Normal(Vec3 surfacePoint) => (surfacePoint - ClosestAxisPoint(surfacePoint)).Normalized;

        /// <summary>
        /// Where the line <c>origin + t * direction</c> runs inside the capsule: from t = <paramref name="enter"/>
        /// to t = <paramref name="exit"/>, either of which may be negative (behind the origin). Returns false,
        /// with an empty interval (enter = +infinity, exit = -infinity), when the line misses.
        /// <paramref name="direction"/> must be a unit vector.
        /// </summary>
        public bool Intersect(Vec3 origin, Vec3 direction, out double enter, out double exit)
        {
            // Solve from the point of the line nearest the capsule's centre, so that every quantity is about
            // as large as the capsule and far-away origins cost no precision.
            double shift = Vec3.Dot((A + B) * 0.5 - origin, direction);
            Vec3 o = origin + direction * shift;
            enter = double.PositiveInfinity;
            exit = double.NegativeInfinity;
            // The capsule is convex and the union of its two end spheres and its cylinder, so the line's
            // interval inside it runs from the earliest entry into a piece to the latest exit.
            AddSphere(o, direction, A, ref enter, ref exit);
            if (length > MinLength)
            {
                AddSphere(o, direction, B, ref enter, ref exit);
                AddCylinder(o, direction, ref enter, ref exit);
            }
            if (enter > exit)
            {
                return false;
            }
            enter += shift;
            exit += shift;
            return true;
        }

        private void AddSphere(Vec3 o, Vec3 d, Vec3 centre, ref double enter, ref double exit)
        {
            Vec3 oc = o - centre;
            double along = Vec3.Dot(oc, d);
            // squared distance of the centre from the line, computed directly to avoid cancellation
            double h = Radius * Radius - (oc - d * along).LengthSquared;
            if (h < 0)
            {
                return;
            }
            h = Math.Sqrt(h);
            enter = Math.Min(enter, -along - h);
            exit = Math.Max(exit, -along + h);
        }

        private void AddCylinder(Vec3 o, Vec3 d, ref double enter, ref double exit)
        {
            Vec3 oa = o - A;
            double dAxis = Vec3.Dot(d, axis);
            double oAxis = Vec3.Dot(oa, axis);

            // between the end planes: 0 <= oAxis + t dAxis <= length
            double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
            if (Math.Abs(dAxis) > 1e-12)
            {
                lo = -oAxis / dAxis;
                hi = (length - oAxis) / dAxis;
                if (lo > hi)
                {
                    (lo, hi) = (hi, lo);
                }
            }
            else if (oAxis < 0 || oAxis > length)
            {
                return;
            }

            // within the radius of the axis line: |op + t dp| <= r
            Vec3 dp = d - axis * dAxis;
            Vec3 op = oa - axis * oAxis;
            double a = dp.LengthSquared;
            double c = op.LengthSquared - Radius * Radius;
            if (a > 1e-24)
            {
                double b = Vec3.Dot(op, dp);
                double disc = b * b - a * c;
                if (disc < 0)
                {
                    return;
                }
                double s = Math.Sqrt(disc);
                lo = Math.Max(lo, (-b - s) / a);
                hi = Math.Min(hi, (-b + s) / a);
            }
            else if (c > 0)
            {
                return; // parallel to the axis, outside the radius
            }

            if (lo <= hi)
            {
                enter = Math.Min(enter, lo);
                exit = Math.Max(exit, hi);
            }
        }

        private Vec3 ClosestAxisPoint(Vec3 p)
        {
            if (length <= MinLength)
            {
                return A;
            }
            double t = MathUtil.Clamp(Vec3.Dot(p - A, axis), 0.0, length);
            return A + axis * t;
        }
    }
}

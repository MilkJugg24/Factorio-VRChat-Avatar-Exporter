using System;

namespace FactorioAvatarExporter.Core
{
    public static class Ik
    {
        /// <summary>
        /// Two-bone IK, a literal port of <c>two_bone()</c> in tools/dragon_render/model.py.
        /// Returns the middle joint and the end point for a chain from <paramref name="root"/> towards
        /// <paramref name="target"/> with segment lengths <paramref name="l1"/> and <paramref name="l2"/>;
        /// the joint bends towards <paramref name="pole"/> (a direction). Out-of-reach targets are
        /// clamped to just inside the reachable range.
        /// </summary>
        public static (Vec3 joint, Vec3 end) TwoBone(Vec3 root, Vec3 target, double l1, double l2, Vec3 pole)
        {
            Vec3 d = target - root;
            double dist = d.Length;
            Vec3 dirn = d / Math.Max(dist, 1e-6);
            dist = Math.Min(Math.Max(dist, Math.Abs(l1 - l2) + 1e-3), l1 + l2 - 1e-3);
            double cosA = (l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist);
            double a = Math.Acos(MathUtil.Clamp(cosA, -1.0, 1.0));
            Vec3 pp = pole - dirn * Vec3.Dot(pole, dirn);
            if (pp.Length < 1e-6)
            {
                pp = Vec3.Cross(dirn, Vec3.UnitX);
            }
            pp = pp.Normalized;
            Vec3 joint = root + dirn * (Math.Cos(a) * l1) + pp * (Math.Sin(a) * l1);
            Vec3 end = root + dirn * dist;
            return (joint, end);
        }
    }
}

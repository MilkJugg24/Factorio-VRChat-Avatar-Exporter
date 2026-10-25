using System;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Foundation
{
    public class MathTests
    {
        private const double Eps = 1e-12;

        [Test]
        public void QuatRotationMatchesRodrigues()
        {
            var rng = new Random(1);
            for (int i = 0; i < 200; i++)
            {
                var axis = new Vec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5);
                double angle = (rng.NextDouble() - 0.5) * 10;
                var v = new Vec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5);
                Vec3 a = Quat.FromAxisAngle(axis, angle).Rotate(v);
                Vec3 b = MathUtil.Rot(v, axis, angle);
                Assert.That(a.ApproximatelyEquals(b, 1e-12), $"{a} vs {b}");
            }
        }

        [Test]
        public void RightHandRule()
        {
            // a quarter turn about +z takes +x to +y
            Vec3 r = Quat.FromAxisAngle(Vec3.UnitZ, Math.PI / 2).Rotate(Vec3.UnitX);
            Assert.That(r.ApproximatelyEquals(Vec3.UnitY, Eps), r.ToString());
        }

        [Test]
        public void CompositionOrder()
        {
            Quat a = Quat.FromAxisAngle(Vec3.UnitZ, 0.7);
            Quat b = Quat.FromAxisAngle(Vec3.UnitX, -1.1);
            var v = new Vec3(0.3, -0.2, 0.9);
            Assert.That((a * b).Rotate(v).ApproximatelyEquals(a.Rotate(b.Rotate(v)), Eps));
            Assert.That((a * a.Inverse).ApproximatelyEquals(Quat.Identity, 1e-9));
        }

        [Test]
        public void FromBasisMapsAxes()
        {
            Quat q = Quat.FromAxisAngle(new Vec3(1, 2, 3), 1.234);
            Quat p = Quat.FromBasis(q.Right, q.Forward, q.Up);
            Assert.That(p.ApproximatelyEquals(q, 1e-9), $"{p} vs {q}");
            // not quite orthogonal input keeps forward exactly
            Quat s = Quat.FromBasis(Vec3.UnitX, new Vec3(0, 1, 0.2), Vec3.UnitZ);
            Assert.That(s.Forward.ApproximatelyEquals(new Vec3(0, 1, 0.2).Normalized, Eps));
        }

        [Test]
        public void FromToTurnsDirections()
        {
            var a = new Vec3(0.2, 0.5, -0.4);
            var b = new Vec3(-0.9, 0.1, 0.3);
            Assert.That(Quat.FromTo(a, b).Rotate(a.Normalized).ApproximatelyEquals(b.Normalized, 1e-12));
            Assert.That(Quat.FromTo(a, -a).Rotate(a.Normalized).ApproximatelyEquals(-a.Normalized, 1e-12));
            Assert.That(Quat.FromTo(a, a * 3).ApproximatelyEquals(Quat.Identity, 1e-12));
        }

        [Test]
        public void SlerpEndsAndMiddle()
        {
            Quat a = Quat.FromAxisAngle(Vec3.UnitY, 0.2);
            Quat b = Quat.FromAxisAngle(Vec3.UnitY, 1.4);
            Assert.That(Quat.Slerp(a, b, 0).ApproximatelyEquals(a, 1e-9));
            Assert.That(Quat.Slerp(a, b, 1).ApproximatelyEquals(b, 1e-9));
            Assert.That(Quat.Slerp(a, b, 0.5).ApproximatelyEquals(Quat.FromAxisAngle(Vec3.UnitY, 0.8), 1e-9));
        }

        [Test]
        public void NormalizedKeepsZeroLikePythonNorm()
        {
            Assert.That(Vec3.Zero.Normalized, Is.EqualTo(Vec3.Zero));
            Assert.That(new Vec3(3, 4, 0).Normalized.ApproximatelyEquals(new Vec3(0.6, 0.8, 0), Eps));
        }

        [Test]
        public void TwoBoneReachesAndBendsTowardsPole()
        {
            var root = new Vec3(0, 0, 1);
            var target = new Vec3(0.2, 0.3, 0.6);
            (Vec3 joint, Vec3 end) = Ik.TwoBone(root, target, 0.3, 0.3, Vec3.UnitY);
            Assert.That(end.ApproximatelyEquals(target, 1e-12));
            Assert.That(Vec3.Distance(root, joint), Is.EqualTo(0.3).Within(1e-12));
            Assert.That(Vec3.Distance(joint, end), Is.EqualTo(0.3).Within(1e-12));
            // too far: clamped to l1 + l2 - 1e-3
            (_, Vec3 far) = Ik.TwoBone(root, root + new Vec3(0, 0, -5), 0.3, 0.3, Vec3.UnitY);
            Assert.That(Vec3.Distance(root, far), Is.EqualTo(0.599).Within(1e-12));
        }

        [Test]
        public void SmoothstepAndModulo()
        {
            Assert.That(MathUtil.Smoothstep(0, 1, -1), Is.EqualTo(0));
            Assert.That(MathUtil.Smoothstep(0, 1, 0.5), Is.EqualTo(0.5));
            Assert.That(MathUtil.Smoothstep(0, 1, 2), Is.EqualTo(1));
            Assert.That(MathUtil.PositiveModulo(-0.25, 1.0), Is.EqualTo(0.75));
        }
    }

    public class ConventionTests
    {
        [Test]
        public void OrientationTurnsClockwise()
        {
            // facing east (0.25), the model's forward points east and its right points south
            Assert.That(Conventions.ModelPointToWorld(Vec3.UnitY, 0.25, false).ApproximatelyEquals(Vec3.UnitX, 1e-12));
            Assert.That(Conventions.ModelPointToWorld(Vec3.UnitX, 0.25, false).ApproximatelyEquals(-Vec3.UnitY, 1e-12));
            Assert.That(Conventions.ModelPointToWorld(Vec3.UnitX, 0.0, true).ApproximatelyEquals(-Vec3.UnitX, 1e-12));
        }

        [Test]
        public void ShadowLightMatchesTheEngineers()
        {
            // normalize(-1.13, 0.05, 1.0) with y pointing south, measured on the vanilla engineer's shadow
            Vec3 l = Conventions.ShadowLight;
            double n = Math.Sqrt(1.13 * 1.13 + 0.05 * 0.05 + 1.0);
            Assert.That(l.ApproximatelyEquals(new Vec3(-1.13 / n, -0.05 / n, 1.0 / n), 1e-12));
            Assert.That(Math.Asin(l.Z) * 180 / Math.PI, Is.EqualTo(41.5).Within(0.1), "degrees above the horizon");
            Assert.That(l.X, Is.LessThan(0), "light from the west, shadows fall east");
            Assert.That(l.Y, Is.LessThan(0), "light a little from the south, shadows fall a little north");
        }

        [Test]
        public void UnitySpaceRoundTrips()
        {
            var v = new Vec3(0.1, 0.2, 0.3);
            (double x, double y, double z) = UnitySpace.ToUnity(v);
            Assert.That(UnitySpace.ToCore(x, y, z), Is.EqualTo(v));
            Quat q = Quat.FromAxisAngle(new Vec3(0.3, -0.5, 0.8), 0.9);
            (double qx, double qy, double qz, double qw) = UnitySpace.RotationToUnity(q);
            Assert.That(UnitySpace.RotationToCore(qx, qy, qz, qw).ApproximatelyEquals(q, 1e-12));
        }

        [Test]
        public void UnitySpaceRotationKeepsPhysicalSense()
        {
            // Core: a quarter turn counterclockwise seen from above (about +z) takes east to north.
            Quat core = Quat.FromAxisAngle(Vec3.UnitZ, Math.PI / 2);
            (double x, double y, double z, double w) = UnitySpace.RotationToUnity(core);
            // Unity is left-handed: the same physical turn is -90 degrees about +y, i.e.
            // (0, -sin 45, 0, cos 45), and it takes Unity east (1,0,0) to Unity north (0,0,1).
            Assert.That(x, Is.EqualTo(0).Within(1e-12));
            Assert.That(y, Is.EqualTo(-Math.Sqrt(0.5)).Within(1e-12));
            Assert.That(z, Is.EqualTo(0).Within(1e-12));
            Assert.That(w, Is.EqualTo(Math.Sqrt(0.5)).Within(1e-12));
            Vec3 north = UnityRotate((x, y, z, w), (1, 0, 0));
            Assert.That(north.ApproximatelyEquals(new Vec3(0, 0, 1), 1e-12), north.ToString());
        }

        // Unity's quaternion * vector (same formula as Quat.Rotate, applied to raw components;
        // Unity's left-handedness only changes how the numbers are interpreted).
        private static Vec3 UnityRotate((double x, double y, double z, double w) q, (double x, double y, double z) v)
        {
            return new Quat(q.w, q.x, q.y, q.z).Rotate(new Vec3(v.x, v.y, v.z));
        }

        [Test]
        public void BoundsCornersAndUnion()
        {
            Bounds3 b = Bounds3.Empty.Encapsulate(new Vec3(1, 2, 3)).Encapsulate(new Vec3(-1, 0, 5));
            Assert.That(b.Min, Is.EqualTo(new Vec3(-1, 0, 3)));
            Assert.That(b.Max, Is.EqualTo(new Vec3(1, 2, 5)));
            Assert.That(b.Corners().Length, Is.EqualTo(8));
            Assert.That(Bounds3.Empty.Corners().Length, Is.EqualTo(0));
            Assert.That(Bounds3.Empty.Union(b).Min, Is.EqualTo(b.Min));
        }
    }

    public class AnimationSpecTests
    {
        [Test]
        public void MatchesPythonTables()
        {
            // tools/dragon_render/anims.py ANIMATIONS plus the corpse; the mine is the engineer's 26 frames
            Assert.That(AnimationSpec.Get("run").FrameCount, Is.EqualTo(12));
            Assert.That(AnimationSpec.Get("mine").FrameCount, Is.EqualTo(26));
            Assert.That(AnimationSpec.Get("run-gun").DirectionCount, Is.EqualTo(18));
            Assert.That(AnimationSpec.Get("fly-gun").FrameCount, Is.EqualTo(8));
            Assert.That(AnimationSpec.Get("take-off").Loop, Is.False);
            // plus the sprint, which the dragon renderer has not
            Assert.That(AnimationSpec.All.Count, Is.EqualTo(12));
            Assert.That(AnimationSpec.Get("sprint").FrameCount, Is.EqualTo(AnimationSpec.Get("run").FrameCount));
        }

        [Test]
        public void GunRows()
        {
            DirectionInfo d = AnimationSpec.Direction("run-gun", 6); // (1, 3)
            Assert.That(d.Orientation, Is.EqualTo(1 / 8.0));
            Assert.That(d.Move, Is.EqualTo(2 * MathUtil.Tau / 8.0).Within(1e-15));
            DirectionInfo e = AnimationSpec.Direction("run", 3);
            Assert.That(e.Orientation, Is.EqualTo(3 / 8.0));
            Assert.That(e.Move, Is.EqualTo(0));
        }

        [Test]
        public void FramePhases()
        {
            Assert.That(AnimationSpec.FramePhase("run", 6), Is.EqualTo(0.5));
            Assert.That(AnimationSpec.FramePhase("take-off", 7), Is.EqualTo(1.0));
            Assert.That(AnimationSpec.FramePhase("corpse", 0), Is.EqualTo(0.0));
        }

        [Test]
        public void PackSets()
        {
            Assert.That(System.Linq.Enumerable.Count(AnimationSpec.ForPack(false)), Is.EqualTo(7));
            Assert.That(System.Linq.Enumerable.Count(AnimationSpec.ForPack(true)), Is.EqualTo(12));
        }
    }
}

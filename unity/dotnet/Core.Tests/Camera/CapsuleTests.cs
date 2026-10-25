using System;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Camera
{
    public class CapsuleTests
    {
        private const double Eps = 1e-9;

        private static Vec3 RandomVector(Random rng, double extent) =>
            new Vec3((rng.NextDouble() * 2 - 1) * extent, (rng.NextDouble() * 2 - 1) * extent,
                (rng.NextDouble() * 2 - 1) * extent);

        private static Vec3 RandomDirection(Random rng)
        {
            while (true)
            {
                Vec3 v = RandomVector(rng, 1);
                if (v.Length > 0.1 && v.Length < 1)
                {
                    return v.Normalized;
                }
            }
        }

        [Test]
        public void HoldsItsShape()
        {
            var c = new Capsule(new Vec3(1, 2, 3), new Vec3(1, 2, 5), 0.5, StickPart.LeftLeg);
            Assert.That(c.A, Is.EqualTo(new Vec3(1, 2, 3)));
            Assert.That(c.B, Is.EqualTo(new Vec3(1, 2, 5)));
            Assert.That(c.Radius, Is.EqualTo(0.5));
            Assert.That(c.Part, Is.EqualTo(StickPart.LeftLeg));
            Assert.That(c.Bounds.Min, Is.EqualTo(new Vec3(0.5, 1.5, 2.5)));
            Assert.That(c.Bounds.Max, Is.EqualTo(new Vec3(1.5, 2.5, 5.5)));
        }

        [Test]
        public void DistanceIsSignedDistanceToTheSurface()
        {
            var c = new Capsule(Vec3.Zero, new Vec3(0, 0, 2), 0.5, StickPart.Torso);
            Assert.That(c.Distance(new Vec3(0, 0, 1)), Is.EqualTo(-0.5).Within(Eps), "on the axis");
            Assert.That(c.Distance(new Vec3(2, 0, 1)), Is.EqualTo(1.5).Within(Eps), "beside the cylinder");
            Assert.That(c.Distance(new Vec3(0, 0, 3)), Is.EqualTo(0.5).Within(Eps), "above the top cap");
            Assert.That(c.Distance(new Vec3(0, 0.5, -0.0)), Is.EqualTo(0).Within(Eps), "on the surface");
        }

        [Test]
        public void NormalPointsOutwards()
        {
            var c = new Capsule(Vec3.Zero, new Vec3(0, 0, 2), 0.5, StickPart.Torso);
            Assert.That(c.Normal(new Vec3(0.5, 0, 1)).ApproximatelyEquals(Vec3.UnitX, Eps));
            Assert.That(c.Normal(new Vec3(0, 0, 2.5)).ApproximatelyEquals(Vec3.UnitZ, Eps));
            Assert.That(c.Normal(new Vec3(0, -0.5, -0.0)).ApproximatelyEquals(-Vec3.UnitY, Eps));
        }

        [Test]
        public void TranslatedMovesEverything()
        {
            var c = new Capsule(Vec3.Zero, new Vec3(1, 0, 0), 0.2, StickPart.Prop);
            Capsule t = c.Translated(new Vec3(0, 0, 3));
            Assert.That(t.A, Is.EqualTo(new Vec3(0, 0, 3)));
            Assert.That(t.B, Is.EqualTo(new Vec3(1, 0, 3)));
            Assert.That(t.Radius, Is.EqualTo(0.2));
            Assert.That(t.Part, Is.EqualTo(StickPart.Prop));
        }

        [Test]
        public void RayAlongTheAxisRunsThroughBothCaps()
        {
            var c = new Capsule(Vec3.Zero, new Vec3(0, 0, 2), 0.5, StickPart.Torso);
            Assert.That(c.Intersect(new Vec3(0, 0, -10), Vec3.UnitZ, out double enter, out double exit), Is.True);
            Assert.That(enter, Is.EqualTo(9.5).Within(Eps));
            Assert.That(exit, Is.EqualTo(12.5).Within(Eps));
        }

        [Test]
        public void RayAcrossTheCylinder()
        {
            var c = new Capsule(Vec3.Zero, new Vec3(0, 0, 2), 0.5, StickPart.Torso);
            // passes 0.3 from the axis: half chord sqrt(0.25 - 0.09) = 0.4
            Assert.That(c.Intersect(new Vec3(-5, 0.3, 1), Vec3.UnitX, out double enter, out double exit), Is.True);
            Assert.That(enter, Is.EqualTo(4.6).Within(Eps));
            Assert.That(exit, Is.EqualTo(5.4).Within(Eps));
            // behind the origin
            Assert.That(c.Intersect(new Vec3(5, 0.3, 1), Vec3.UnitX, out enter, out exit), Is.True);
            Assert.That(enter, Is.EqualTo(-5.4).Within(Eps));
            Assert.That(exit, Is.EqualTo(-4.6).Within(Eps));
        }

        [Test]
        public void MissesGiveAnEmptyInterval()
        {
            var c = new Capsule(Vec3.Zero, new Vec3(0, 0, 2), 0.5, StickPart.Torso);
            Assert.That(c.Intersect(new Vec3(-5, 0.6, 1), Vec3.UnitX, out double enter, out double exit), Is.False);
            Assert.That(enter, Is.EqualTo(double.PositiveInfinity));
            Assert.That(exit, Is.EqualTo(double.NegativeInfinity));
            Assert.That(c.Intersect(new Vec3(0, 0, 3), Vec3.UnitX, out _, out _), Is.False, "above the top cap");
            Assert.That(c.Intersect(new Vec3(0.6, 0, -4), Vec3.UnitZ, out _, out _), Is.False, "parallel, outside");
        }

        [Test]
        public void SpheresIntersectLikeSpheres()
        {
            var c = new Capsule(new Vec3(1, 1, 1), new Vec3(1, 1, 1), 0.25, StickPart.Head);
            Assert.That(c.Intersect(new Vec3(1, -3, 1), Vec3.UnitY, out double enter, out double exit), Is.True);
            Assert.That(enter, Is.EqualTo(3.75).Within(Eps));
            Assert.That(exit, Is.EqualTo(4.25).Within(Eps));
            Assert.That(c.Normal(new Vec3(1, 1.25, 1)).ApproximatelyEquals(Vec3.UnitY, Eps));
        }

        [Test]
        public void IntersectAgreesWithTheDistanceFunction()
        {
            var rng = new Random(331);
            int hits = 0;
            for (int i = 0; i < 2000; i++)
            {
                Vec3 a = RandomVector(rng, 1);
                Vec3 b = rng.Next(5) == 0 ? a : a + RandomVector(rng, 1);
                var c = new Capsule(a, b, 0.05 + rng.NextDouble() * 0.5, StickPart.Torso);
                Vec3 direction = RandomDirection(rng);
                // a line through a point near the capsule, its origin far back along the line
                Vec3 through = Vec3.Lerp(a, b, rng.NextDouble()) + RandomVector(rng, 0.8);
                Vec3 origin = through - direction * (rng.NextDouble() * 30);

                bool hit = c.Intersect(origin, direction, out double enter, out double exit);
                double closest = MinimumDistanceAlongLine(c, origin, direction);
                if (hit)
                {
                    hits++;
                    Assert.That(enter, Is.LessThanOrEqualTo(exit));
                    Assert.That(c.Distance(origin + direction * enter), Is.EqualTo(0).Within(1e-7), "enters on the surface");
                    Assert.That(c.Distance(origin + direction * exit), Is.EqualTo(0).Within(1e-7), "exits on the surface");
                    Assert.That(c.Distance(origin + direction * ((enter + exit) / 2)), Is.LessThanOrEqualTo(1e-9), "inside between");
                    Assert.That(c.Distance(origin + direction * (enter - 1e-4)), Is.GreaterThan(0), "outside before");
                    Assert.That(c.Distance(origin + direction * (exit + 1e-4)), Is.GreaterThan(0), "outside after");
                }
                else
                {
                    Assert.That(closest, Is.GreaterThan(-1e-6), "a miss never passes inside");
                }
            }
            Assert.That(hits, Is.GreaterThan(400), "the sample hits often enough to mean something");
        }

        [Test]
        public void NormalsAtHitPointsFaceTheRay()
        {
            var rng = new Random(332);
            for (int i = 0; i < 500; i++)
            {
                Vec3 a = RandomVector(rng, 1);
                var c = new Capsule(a, a + RandomVector(rng, 1), 0.3, StickPart.Torso);
                Vec3 direction = RandomDirection(rng);
                Vec3 origin = (c.A + c.B) * 0.5 + RandomVector(rng, 0.2) - direction * 10;
                if (!c.Intersect(origin, direction, out double enter, out _))
                {
                    continue;
                }
                Vec3 n = c.Normal(origin + direction * enter);
                Assert.That(n.Length, Is.EqualTo(1).Within(1e-9));
                Assert.That(Vec3.Dot(n, direction), Is.LessThanOrEqualTo(1e-9), "front faces face the viewer");
            }
        }

        [Test]
        public void RejectsBadShapes()
        {
            Assert.That(() => new Capsule(Vec3.Zero, Vec3.UnitX, 0, StickPart.Torso), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Capsule(Vec3.Zero, Vec3.UnitX, -1, StickPart.Torso), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Capsule(Vec3.Zero, Vec3.UnitX, double.NaN, StickPart.Torso), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Capsule(Vec3.Zero, Vec3.UnitX, double.PositiveInfinity, StickPart.Torso),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Capsule(new Vec3(double.NaN, 0, 0), Vec3.UnitX, 1, StickPart.Torso),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("a"));
            Assert.That(() => new Capsule(Vec3.Zero, new Vec3(0, double.NegativeInfinity, 0), 1, StickPart.Torso),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("b"));
        }

        /// <summary>The smallest signed distance of the line to the capsule, by dense sampling then refining.</summary>
        private static double MinimumDistanceAlongLine(Capsule c, Vec3 origin, Vec3 direction)
        {
            double centre = Vec3.Dot((c.A + c.B) * 0.5 - origin, direction);
            double best = double.PositiveInfinity;
            double bestT = centre;
            for (int k = -2000; k <= 2000; k++)
            {
                double t = centre + k * 0.001;
                double d = c.Distance(origin + direction * t);
                if (d < best)
                {
                    best = d;
                    bestT = t;
                }
            }
            for (int k = -100; k <= 100; k++)
            {
                best = Math.Min(best, c.Distance(origin + direction * (bestT + k * 0.00001)));
            }
            return best;
        }
    }
}

using System;
using NUnit.Framework;

namespace MilkJugg24.FactorioAvatarExporter.Core.Tests.Camera
{
    public class PlanarShadowTests
    {
        private const double Eps = 1e-12;

        private static Vec3 RandomPoint(Random rng) =>
            new Vec3(rng.NextDouble() * 6 - 3, rng.NextDouble() * 6 - 3, rng.NextDouble() * 4 - 1);

        private static Vec3 Apply(double[] m, Vec3 p) => new Vec3(
            m[0] * p.X + m[1] * p.Y + m[2] * p.Z + m[3],
            m[4] * p.X + m[5] * p.Y + m[6] * p.Z + m[7],
            m[8] * p.X + m[9] * p.Y + m[10] * p.Z + m[11]);

        [Test]
        public void GroundPointsCastShadowsOnThemselves()
        {
            var rng = new Random(311);
            for (int i = 0; i < 100; i++)
            {
                Vec3 p = RandomPoint(rng).WithZ(0);
                Assert.That(PlanarShadow.Project(p), Is.EqualTo(p));
            }
        }

        [Test]
        public void ShadowsAreOnTheGroundAlongTheLight()
        {
            var rng = new Random(312);
            Vec3 light = Conventions.ShadowLight;
            for (int i = 0; i < 300; i++)
            {
                Vec3 p = RandomPoint(rng);
                Vec3 shadow = PlanarShadow.Project(p);
                Assert.That(shadow.Z, Is.EqualTo(0));
                Assert.That(shadow.ApproximatelyEquals(p - light * (p.Z / light.Z), Eps), "P - L * (P.z / L.z)");
                Assert.That(Vec3.Cross(p - shadow, light).Length, Is.LessThan(Eps), "the point lies on the light ray");
            }
        }

        [Test]
        public void ShadowsFallEastAndSlightlyNorth()
        {
            Vec3 shadow = PlanarShadow.Project(new Vec3(0, 0, 1.0));
            // the light (-1.13, 0.05, 1.0) with y pointing south is (-1.13, -0.05, 1.0)
            Assert.That(shadow.ApproximatelyEquals(new Vec3(1.13, 0.05, 0), Eps), shadow.ToString());
        }

        [Test]
        public void WorldMatrixIsProject()
        {
            var rng = new Random(313);
            double[] m = PlanarShadow.WorldMatrix();
            Assert.That(m.Length, Is.EqualTo(16));
            Assert.That(new[] { m[12], m[13], m[14], m[15] }, Is.EqualTo(new double[] { 0, 0, 0, 1 }), "affine");
            for (int i = 0; i < 200; i++)
            {
                Vec3 p = RandomPoint(rng);
                Assert.That(Apply(m, p).ApproximatelyEquals(PlanarShadow.Project(p), Eps));
            }
        }

        [Test]
        public void UnityMatrixIsProjectInUnitySpace()
        {
            var rng = new Random(314);
            double[] m = PlanarShadow.UnityMatrix();
            Assert.That(new[] { m[12], m[13], m[14], m[15] }, Is.EqualTo(new double[] { 0, 0, 0, 1 }), "affine");
            for (int i = 0; i < 200; i++)
            {
                Vec3 p = RandomPoint(rng);
                (double x, double y, double z) unity = UnitySpace.ToUnity(p);
                Vec3 flattened = Apply(m, new Vec3(unity.x, unity.y, unity.z));
                Assert.That(flattened.Y, Is.EqualTo(0), "flattened onto Unity's ground y = 0");
                Vec3 core = UnitySpace.ToCore(flattened.X, flattened.Y, flattened.Z);
                Assert.That(core.ApproximatelyEquals(PlanarShadow.Project(p), Eps), core + " vs " + PlanarShadow.Project(p));
            }
        }

        [Test]
        public void ProjectIsLinearSoAnyLengthUnitWorks()
        {
            var rng = new Random(315);
            for (int i = 0; i < 50; i++)
            {
                Vec3 p = RandomPoint(rng);
                double scale = rng.NextDouble() * 3 + 0.1;
                Assert.That(PlanarShadow.Project(p * scale).ApproximatelyEquals(PlanarShadow.Project(p) * scale, 1e-12));
            }
        }

        [Test]
        public void MatricesAreNewArraysEachCall()
        {
            double[] a = PlanarShadow.WorldMatrix();
            a[0] = 99;
            Assert.That(PlanarShadow.WorldMatrix()[0], Is.EqualTo(1));
            double[] b = PlanarShadow.UnityMatrix();
            b[0] = 99;
            Assert.That(PlanarShadow.UnityMatrix()[0], Is.EqualTo(1));
        }
    }
}

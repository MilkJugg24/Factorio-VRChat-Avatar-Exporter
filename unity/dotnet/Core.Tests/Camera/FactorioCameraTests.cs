using System;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Camera
{
    public class FactorioCameraTests
    {
        private const double Eps = 1e-9;

        private static readonly CanvasSpec Canvas = new CanvasSpec(224, 256, 112, 200, 64, 2);

        /// <summary>render.py's <c>camera()</c>, literally: R, U, D in the Python world (y south).</summary>
        private static (Vec3 r, Vec3 u, Vec3 d, double stretch) PythonCamera()
        {
            double phi = MathUtil.Radians(45.0);
            return (new Vec3(1, 0, 0), new Vec3(0, -Math.Sin(phi), Math.Cos(phi)),
                new Vec3(0, -Math.Cos(phi), -Math.Sin(phi)), 1.25);
        }

        /// <summary>
        /// The final pixel position of a Python world point: sdf.py's <c>render</c> maps pixel centre
        /// (ix + 0.5, iy + 0.5) to sx = (ix + 0.5 - ax) / ppt, sy = (ay - (iy + 0.5)) / ppt / stretch, the ray
        /// through sx R + sy U along D; inverted with the orthonormal R, U.
        /// </summary>
        private static (double x, double y) PythonPixel(Vec3 pythonWorld, CanvasSpec canvas)
        {
            (Vec3 r, Vec3 u, _, double stretch) = PythonCamera();
            double sx = Vec3.Dot(pythonWorld, r);
            double sy = Vec3.Dot(pythonWorld, u);
            return (canvas.AnchorX + sx * canvas.PixelsPerTile, canvas.AnchorY - sy * canvas.PixelsPerTile * stretch);
        }

        /// <summary>sdf.py's view ray origin for final pixel position (x, y), Python world.</summary>
        private static Vec3 PythonRayOrigin(double x, double y, CanvasSpec canvas)
        {
            (Vec3 r, Vec3 u, Vec3 d, double stretch) = PythonCamera();
            double sx = (x - canvas.AnchorX) / canvas.PixelsPerTile;
            double sy = (canvas.AnchorY - y) / canvas.PixelsPerTile / stretch;
            return r * sx + u * sy - d * 6.0;
        }

        private static Vec3 RandomPoint(Random rng, double extent) =>
            new Vec3((rng.NextDouble() - 0.5) * 2 * extent, (rng.NextDouble() - 0.5) * 2 * extent,
                (rng.NextDouble() - 0.5) * 2 * extent);

        [Test]
        public void AxesArePythonsCameraWithYTurnedNorth()
        {
            (Vec3 r, Vec3 u, Vec3 d, double stretch) = PythonCamera();
            Assert.That(FactorioCamera.Right.ApproximatelyEquals(Conventions.PythonWorldToWorld(r), Eps));
            Assert.That(FactorioCamera.Up.ApproximatelyEquals(Conventions.PythonWorldToWorld(u), Eps));
            Assert.That(FactorioCamera.Forward.ApproximatelyEquals(Conventions.PythonWorldToWorld(d), Eps));
            Assert.That(Conventions.VerticalStretch, Is.EqualTo(stretch));
            double s = Math.Sqrt(0.5);
            Assert.That(FactorioCamera.Up.ApproximatelyEquals(new Vec3(0, s, s), Eps), "up tilts north");
            Assert.That(FactorioCamera.Forward.ApproximatelyEquals(new Vec3(0, s, -s), Eps), "looks north and down");
        }

        [Test]
        public void AxesAreARightHandedOrthonormalBasis()
        {
            Vec3 r = FactorioCamera.Right, f = FactorioCamera.Forward, u = FactorioCamera.Up;
            Assert.That(r.Length, Is.EqualTo(1).Within(Eps));
            Assert.That(f.Length, Is.EqualTo(1).Within(Eps));
            Assert.That(u.Length, Is.EqualTo(1).Within(Eps));
            Assert.That(Vec3.Dot(r, f), Is.EqualTo(0).Within(Eps));
            Assert.That(Vec3.Dot(r, u), Is.EqualTo(0).Within(Eps));
            Assert.That(Vec3.Dot(f, u), Is.EqualTo(0).Within(Eps));
            Assert.That(Vec3.Cross(r, f).ApproximatelyEquals(u, Eps), "x cross y = z");
        }

        [Test]
        public void ProjectMatchesHandComputedValues()
        {
            double h = 64 * 1.25 * Math.Sqrt(0.5);
            AssertPixel(FactorioCamera.Project(Vec3.Zero, Canvas), 112, 200);
            AssertPixel(FactorioCamera.Project(new Vec3(1, 0, 0), Canvas), 176, 200);
            AssertPixel(FactorioCamera.Project(new Vec3(0, 0, 1), Canvas), 112, 200 - h);
            AssertPixel(FactorioCamera.Project(new Vec3(0, 1, 0), Canvas), 112, 200 - h);
            AssertPixel(FactorioCamera.Project(new Vec3(-0.5, -1, 0.25), Canvas), 80, 200 + 0.75 * h);
        }

        [Test]
        public void ProjectMatchesThePythonRendererForRandomPoints()
        {
            var rng = new Random(301);
            foreach (CanvasSpec canvas in new[] { Canvas, new CanvasSpec(40, 30, -7, 55, 32, 3) })
            {
                for (int i = 0; i < 500; i++)
                {
                    Vec3 python = RandomPoint(rng, 3);
                    (double px, double py) = PythonPixel(python, canvas);
                    AssertPixel(FactorioCamera.Project(Conventions.PythonWorldToWorld(python), canvas), px, py);
                }
            }
        }

        [Test]
        public void ProjectIsTheAnchorPlusTheOffset()
        {
            var rng = new Random(302);
            for (int i = 0; i < 100; i++)
            {
                Vec3 p = RandomPoint(rng, 2);
                (double dx, double dy) = FactorioCamera.OffsetFromAnchor(p, Canvas.PixelsPerTile);
                AssertPixel(FactorioCamera.Project(p, Canvas), Canvas.AnchorX + dx, Canvas.AnchorY + dy);
            }
        }

        [Test]
        public void PointsAlongTheViewDirectionLandOnTheSamePixel()
        {
            var rng = new Random(303);
            for (int i = 0; i < 100; i++)
            {
                Vec3 p = RandomPoint(rng, 2);
                (double x, double y) = FactorioCamera.Project(p, Canvas);
                AssertPixel(FactorioCamera.Project(p + FactorioCamera.Forward * (rng.NextDouble() * 10 - 5), Canvas), x, y);
            }
        }

        [Test]
        public void UnprojectInvertsProjectOnTheCameraPlane()
        {
            var rng = new Random(304);
            for (int i = 0; i < 200; i++)
            {
                double x = rng.NextDouble() * 300 - 30;
                double y = rng.NextDouble() * 300 - 30;
                Vec3 p = FactorioCamera.Unproject(x, y, Canvas);
                Assert.That(Vec3.Dot(p, FactorioCamera.Forward), Is.EqualTo(0).Within(Eps), "on the camera plane");
                AssertPixel(FactorioCamera.Project(p, Canvas), x, y);
            }
        }

        [Test]
        public void ViewRaysAreSdfRendersRays()
        {
            var rng = new Random(305);
            for (int i = 0; i < 200; i++)
            {
                double x = rng.NextDouble() * 224;
                double y = rng.NextDouble() * 256;
                Vec3 origin = FactorioCamera.ViewRayOrigin(x, y, Canvas);
                Vec3 python = Conventions.PythonWorldToWorld(PythonRayOrigin(x, y, Canvas));
                Vec3 between = python - origin;
                Vec3 across = between - FactorioCamera.Forward * Vec3.Dot(between, FactorioCamera.Forward);
                Assert.That(across.Length, Is.LessThan(Eps), "the same line");
                Assert.That(Vec3.Dot(origin, FactorioCamera.Forward), Is.EqualTo(-FactorioCamera.BackOffTiles).Within(Eps));
                AssertPixel(FactorioCamera.Project(origin, Canvas), x, y);
            }
        }

        [Test]
        public void GroundPointIsWhereSdfFindsTheGround()
        {
            var rng = new Random(306);
            (_, _, Vec3 d, _) = PythonCamera();
            for (int i = 0; i < 200; i++)
            {
                double x = rng.NextDouble() * 224;
                double y = rng.NextDouble() * 256;
                // render_ground_shadow: t = -oz / Dz, ground = o + D t
                Vec3 o = PythonRayOrigin(x, y, Canvas);
                Vec3 python = o + d * (-o.Z / d.Z);
                Vec3 ground = FactorioCamera.GroundPoint(x, y, Canvas);
                Assert.That(ground.Z, Is.EqualTo(0));
                Assert.That(ground.ApproximatelyEquals(Conventions.PythonWorldToWorld(python), Eps), ground + " vs " + python);
                AssertPixel(FactorioCamera.Project(ground, Canvas), x, y);
            }
        }

        [Test]
        public void SetupHasTheDocumentedPoseAndExtents()
        {
            CameraSetup setup = FactorioCamera.Setup(Canvas);
            Assert.That(setup.Rotation.Right.ApproximatelyEquals(FactorioCamera.Right, Eps));
            Assert.That(setup.Rotation.Forward.ApproximatelyEquals(FactorioCamera.Forward, Eps));
            Assert.That(setup.Rotation.Up.ApproximatelyEquals(FactorioCamera.Up, Eps));
            Assert.That(setup.HalfWidth, Is.EqualTo(224 / (2.0 * 64)).Within(Eps));
            Assert.That(setup.HalfHeight, Is.EqualTo(256 / (2.0 * 64 * 1.25)).Within(Eps));
            Assert.That(setup.Near, Is.EqualTo(0));
            Assert.That(setup.Far, Is.EqualTo(40));
            Assert.That(setup.Far - setup.Near, Is.EqualTo(FactorioCamera.DepthTiles));
            Assert.That(setup.TargetWidth, Is.EqualTo(448));
            Assert.That(setup.TargetHeight, Is.EqualTo(512));
            Vec3 centre = FactorioCamera.Unproject(112, 128, Canvas);
            Assert.That(setup.Position.ApproximatelyEquals(centre - FactorioCamera.Forward * 20, Eps), setup.Position.ToString());
        }

        [Test]
        public void SetupSeesWhatProjectShows()
        {
            var rng = new Random(307);
            foreach (CanvasSpec canvas in new[] { Canvas, new CanvasSpec(33, 17, -40, 90, 32, 1) })
            {
                CameraSetup setup = FactorioCamera.Setup(canvas);
                for (int i = 0; i < 300; i++)
                {
                    Vec3 p = RandomPoint(rng, 4);
                    Vec3 v = setup.Rotation.Inverse.Rotate(p - setup.Position);
                    double x = canvas.Width / 2.0 * (1 + v.X / setup.HalfWidth);
                    double y = canvas.Height / 2.0 * (1 - v.Z / setup.HalfHeight);
                    AssertPixel((x, y), FactorioCamera.Project(p, canvas).x, FactorioCamera.Project(p, canvas).y);
                    Assert.That(v.Y, Is.InRange(setup.Near, setup.Far), "points near the origin are inside the slab");
                    Assert.That(v.Y, Is.EqualTo(FactorioCamera.BackOffTiles + Vec3.Dot(p, FactorioCamera.Forward)).Within(Eps));
                }
            }
        }

        [Test]
        public void SetupViewCoversTheCanvasExactly()
        {
            CameraSetup setup = FactorioCamera.Setup(Canvas);
            Vec3 topLeft = setup.Rotation.Inverse.Rotate(FactorioCamera.Unproject(0, 0, Canvas) - setup.Position);
            Vec3 bottomRight = setup.Rotation.Inverse.Rotate(FactorioCamera.Unproject(224, 256, Canvas) - setup.Position);
            Assert.That(topLeft.ApproximatelyEquals(new Vec3(-setup.HalfWidth, 20, setup.HalfHeight), Eps), topLeft.ToString());
            Assert.That(bottomRight.ApproximatelyEquals(new Vec3(setup.HalfWidth, 20, -setup.HalfHeight), Eps), bottomRight.ToString());
        }

        [Test]
        public void UnityCameraLooksAlongForwardWithUpUp()
        {
            UnityCameraSetup unity = FactorioCamera.Setup(Canvas).ToUnity(1.0);
            AssertUnityAxis(unity.Rotation, (0, 0, 1), FactorioCamera.Forward);
            AssertUnityAxis(unity.Rotation, (0, 1, 0), FactorioCamera.Up);
            AssertUnityAxis(unity.Rotation, (1, 0, 0), FactorioCamera.Right);
            // a Unity camera pitched 45 degrees down about its x axis
            double half = MathUtil.Radians(22.5);
            (double x, double y, double z, double w) q = unity.Rotation;
            double sign = Math.Sign(q.w);
            Assert.That(sign * q.x, Is.EqualTo(Math.Sin(half)).Within(Eps));
            Assert.That(q.y, Is.EqualTo(0).Within(Eps));
            Assert.That(q.z, Is.EqualTo(0).Within(Eps));
            Assert.That(sign * q.w, Is.EqualTo(Math.Cos(half)).Within(Eps));
        }

        [Test]
        public void UnityCameraIsScaledToMeters()
        {
            const double metersPerTile = 1.7;
            CameraSetup setup = FactorioCamera.Setup(Canvas);
            UnityCameraSetup unity = setup.ToUnity(metersPerTile);
            Vec3 position = UnitySpace.ToCore(unity.Position.x, unity.Position.y, unity.Position.z);
            Assert.That(position.ApproximatelyEquals(setup.Position * metersPerTile, Eps), position.ToString());
            Quat rotation = UnitySpace.RotationToCore(unity.Rotation.x, unity.Rotation.y, unity.Rotation.z, unity.Rotation.w);
            Assert.That(rotation.ApproximatelyEquals(setup.Rotation, 1e-9));
            Assert.That(unity.HalfWidth, Is.EqualTo(setup.HalfWidth * metersPerTile).Within(Eps));
            Assert.That(unity.HalfHeight, Is.EqualTo(setup.HalfHeight * metersPerTile).Within(Eps));
            Assert.That(unity.Near, Is.EqualTo(setup.Near * metersPerTile).Within(Eps));
            Assert.That(unity.Far, Is.EqualTo(setup.Far * metersPerTile).Within(Eps));
            Assert.That(unity.TargetWidth, Is.EqualTo(setup.TargetWidth));
            Assert.That(unity.TargetHeight, Is.EqualTo(setup.TargetHeight));
        }

        [Test]
        public void UnityCameraRendersPointsWhereProjectPutsThem()
        {
            // Unity: view = inverse(rotation) * (p - position); Matrix4x4.Ortho(-hw, hw, -hh, hh, near, far)
            // maps view x, y to [-1, 1] and keeps depths near..far in front of the camera (its +z)
            const double metersPerTile = 0.6;
            var rng = new Random(308);
            UnityCameraSetup unity = FactorioCamera.Setup(Canvas).ToUnity(metersPerTile);
            (double x, double y, double z, double w) inverse = (-unity.Rotation.x, -unity.Rotation.y, -unity.Rotation.z, unity.Rotation.w);
            for (int i = 0; i < 200; i++)
            {
                Vec3 p = RandomPoint(rng, 3);
                (double ux, double uy, double uz) = UnitySpace.ToUnity(p * metersPerTile);
                (double vx, double vy, double vz) = UnityRotate(inverse,
                    (ux - unity.Position.x, uy - unity.Position.y, uz - unity.Position.z));
                double px = (vx / unity.HalfWidth + 1) / 2 * unity.TargetWidth;
                double py = (1 - vy / unity.HalfHeight) / 2 * unity.TargetHeight;
                (double fx, double fy) = FactorioCamera.Project(p, Canvas);
                AssertPixel((px, py), fx * Canvas.Supersampling, fy * Canvas.Supersampling);
                Assert.That(vz, Is.InRange(unity.Near, unity.Far));
            }
        }

        [TestCase(0.0)]
        [TestCase(-1.0)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void ToUnityRejectsBadScales(double metersPerTile)
        {
            CameraSetup setup = FactorioCamera.Setup(Canvas);
            Assert.That(() => setup.ToUnity(metersPerTile), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void DefaultCanvasIsRejected()
        {
            Assert.That(() => FactorioCamera.Project(Vec3.Zero, default), Throws.ArgumentException);
            Assert.That(() => FactorioCamera.Unproject(0, 0, default), Throws.ArgumentException);
            Assert.That(() => FactorioCamera.ViewRayOrigin(0, 0, default), Throws.ArgumentException);
            Assert.That(() => FactorioCamera.GroundPoint(0, 0, default), Throws.ArgumentException);
            Assert.That(() => FactorioCamera.Setup(default), Throws.ArgumentException);
            Assert.That(() => FactorioCamera.OffsetFromAnchor(Vec3.Zero, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>Unity's <c>Quaternion * Vector3</c> (UnityCsReference Quaternion.cs), written out.</summary>
        internal static (double x, double y, double z) UnityRotate((double x, double y, double z, double w) q,
            (double x, double y, double z) v)
        {
            double x = q.x * 2, y = q.y * 2, z = q.z * 2;
            double xx = q.x * x, yy = q.y * y, zz = q.z * z;
            double xy = q.x * y, xz = q.x * z, yz = q.y * z;
            double wx = q.w * x, wy = q.w * y, wz = q.w * z;
            return ((1 - (yy + zz)) * v.x + (xy - wz) * v.y + (xz + wy) * v.z,
                (xy + wz) * v.x + (1 - (xx + zz)) * v.y + (yz - wx) * v.z,
                (xz - wy) * v.x + (yz + wx) * v.y + (1 - (xx + yy)) * v.z);
        }

        private static void AssertUnityAxis((double x, double y, double z, double w) rotation,
            (double x, double y, double z) unityAxis, Vec3 expectedCore)
        {
            (double x, double y, double z) turned = UnityRotate(rotation, unityAxis);
            Vec3 core = UnitySpace.ToCore(turned.x, turned.y, turned.z);
            Assert.That(core.ApproximatelyEquals(expectedCore, Eps), $"Unity {unityAxis} -> {core}, expected {expectedCore}");
        }

        private static void AssertPixel((double x, double y) actual, double x, double y)
        {
            Assert.That(actual.x, Is.EqualTo(x).Within(1e-9), "x");
            Assert.That(actual.y, Is.EqualTo(y).Within(1e-9), "y");
        }
    }
}

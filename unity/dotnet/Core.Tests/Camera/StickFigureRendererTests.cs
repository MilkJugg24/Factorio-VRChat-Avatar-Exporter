using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Camera
{
    public class StickFigureRendererTests
    {
        private static readonly StickFigureRenderer Renderer = new StickFigureRenderer();

        private static CanvasSpec PlanFor(PoseResult pose, double orientation = 0, bool mirrored = false, int supersampling = 2) =>
            CanvasPlanner.PlanColor(new[] { Renderer.MeasureWorldBounds(pose, orientation, mirrored) }, 64, supersampling);

        [Test]
        public void VerticalCapsuleLandsAtItsProjectedPixels()
        {
            var a = new Vec3(0.3, -0.2, 0.4);
            var b = new Vec3(0.3, -0.2, 1.0);
            PoseResult pose = TestPoses.Segment(a, b);
            CanvasSpec canvas = PlanFor(pose);
            RenderResult result = Renderer.Render(Requests.Color(pose, canvas));
            double radius = StickFigure.Build(pose, 0, false).Capsules.Single().Radius;
            Assert.That(radius, Is.EqualTo(0.07 * 0.6).Within(1e-12), "torso radius for a 0.6 tile figure");

            int inside = 0;
            for (int y = 0; y < canvas.RenderHeight; y++)
            {
                for (int x = 0; x < canvas.RenderWidth; x++)
                {
                    // the view ray through the pixel centre hits iff it passes within the radius of the segment
                    (double fx, double fy) = ReferenceTracer.Centre(canvas, x, y);
                    double distance = LineToSegmentDistance(FactorioCamera.ViewRayOrigin(fx, fy, canvas), FactorioCamera.Forward, a, b);
                    if (distance < radius - 1e-6)
                    {
                        inside++;
                        Assert.That(Pixels.IsFigure(result, x, y), Is.True, $"pixel {x},{y} is on the capsule");
                    }
                    else if (distance > radius + 1e-6)
                    {
                        Assert.That(Pixels.IsFigure(result, x, y), Is.False, $"pixel {x},{y} is beside the capsule");
                    }
                }
            }
            Assert.That(inside, Is.GreaterThan(500));

            // and the drawn pixels span the projected end points widened by the projected radius
            (double ax, double ay) = FactorioCamera.Project(a, canvas);
            (double bx, double by) = FactorioCamera.Project(b, canvas);
            (double cx, double cy, int count) = Pixels.Centroid(result.OnBlack, (x, y) => Pixels.IsFigure(result, x, y));
            Assert.That(count, Is.EqualTo(inside).Within(inside / 20));
            Assert.That(cx / canvas.Supersampling, Is.EqualTo(ax).Within(0.25));
            Assert.That(cy / canvas.Supersampling, Is.EqualTo((ay + by) / 2).Within(0.25));
        }

        [Test]
        public void OnBlackAndOnWhiteDifferOnlyInTheBackground()
        {
            PoseResult pose = TestPoses.TPose();
            CanvasSpec canvas = PlanFor(pose, 0.3);
            RenderResult result = Renderer.Render(Requests.Color(pose, canvas, 0.3));
            Assert.That(result.Linear, Is.False);
            Assert.That(result.Silhouette, Is.Null);
            Assert.That(result.OnBlack.Width, Is.EqualTo(canvas.RenderWidth));
            Assert.That(result.OnBlack.Height, Is.EqualTo(canvas.RenderHeight));
            Assert.That(result.OnWhite.Width, Is.EqualTo(canvas.RenderWidth));
            Assert.That(result.OnWhite.Height, Is.EqualTo(canvas.RenderHeight));
            int figure = 0, background = 0;
            for (int y = 0; y < canvas.RenderHeight; y++)
            {
                for (int x = 0; x < canvas.RenderWidth; x++)
                {
                    var black = Pixels.Get(result.OnBlack, x, y);
                    var white = Pixels.Get(result.OnWhite, x, y);
                    Assert.That(black.a, Is.EqualTo(1f));
                    Assert.That(white.a, Is.EqualTo(1f));
                    if (Pixels.IsFigure(result, x, y))
                    {
                        figure++;
                        Assert.That(white, Is.EqualTo(black), $"opaque pixel {x},{y}");
                    }
                    else
                    {
                        background++;
                        Assert.That(white, Is.EqualTo((1f, 1f, 1f, 1f)), $"background pixel {x},{y}");
                    }
                }
            }
            Assert.That(figure, Is.GreaterThan(1000));
            Assert.That(background, Is.GreaterThan(figure));

            ImageF matte = Matting.FromBlackAndWhite(result.OnBlack, result.OnWhite, result.Linear);
            for (int i = 3; i < matte.Pixels.Length; i += 4)
            {
                Assert.That(matte.Pixels[i], Is.EqualTo(0f).Within(1e-6).Or.EqualTo(1f).Within(1e-6), "alpha is 0 or 1");
            }
        }

        [Test]
        public void LightComesFromTheWest()
        {
            // a lone head sphere: its west (left) half faces the shading light and is brighter
            var pose = new PoseResult();
            pose.Joints[JointNames.Head] = new Vec3(0, 0, 1);
            pose.Joints[JointNames.HeadTop] = new Vec3(0, 0, 1.3);
            CanvasSpec canvas = PlanFor(pose);
            RenderResult result = Renderer.Render(Requests.Color(pose, canvas));
            (double cx, double cy, _) = Pixels.Centroid(result.OnBlack, (x, y) => Pixels.IsFigure(result, x, y));
            double Brightness(int x, int y) => Pixels.Get(result.OnBlack, x, y).g;
            double west = 0, east = 0;
            for (int y = 0; y < canvas.RenderHeight; y++)
            {
                for (int x = 0; x < canvas.RenderWidth; x++)
                {
                    if (Pixels.IsFigure(result, x, y))
                    {
                        if (x + 0.5 < cx)
                        {
                            west += Brightness(x, y);
                        }
                        else
                        {
                            east += Brightness(x, y);
                        }
                    }
                }
            }
            Assert.That(west, Is.GreaterThan(east * 1.2));
            Assert.That(Brightness((int)cx, (int)cy), Is.GreaterThan(0).And.LessThanOrEqualTo(StickPalette.Albedo(StickPart.Head).g + 1e-6));
        }

        [Test]
        public void RaisedCapsuleCastsItsShadowEastOfItsFoot()
        {
            var foot = new Vec3(0, 0, 0.5);
            PoseResult pose = TestPoses.Segment(foot, new Vec3(0, 0, 1.0));
            // plan for the shadow and for the ground point under the foot
            var footprint = new Bounds3(foot.WithZ(0), foot.WithZ(0));
            CanvasSpec canvas = CanvasPlanner.PlanShadow(new[] { Renderer.MeasureWorldBounds(pose, 0, false), footprint }, 64, 2);
            RenderResult result = Renderer.Render(Requests.Shadow(pose, canvas));
            Assert.That(result.OnBlack, Is.Null);
            Assert.That(result.OnWhite, Is.Null);
            Assert.That(result.Linear, Is.False);
            Assert.That(result.Silhouette.Width, Is.EqualTo(canvas.RenderWidth));

            double sumX = 0;
            int count = 0;
            for (int y = 0; y < canvas.RenderHeight; y++)
            {
                for (int x = 0; x < canvas.RenderWidth; x++)
                {
                    var pixel = Pixels.Get(result.Silhouette, x, y);
                    Assert.That(pixel.a, Is.EqualTo(1f));
                    Assert.That(pixel.r == pixel.g && pixel.g == pixel.b && (pixel.r == 0 || pixel.r == 1), "black or white");
                    if (Pixels.IsShadow(result, x, y))
                    {
                        (double fx, double fy) = ReferenceTracer.Centre(canvas, x, y);
                        Vec3 ground = FactorioCamera.GroundPoint(fx, fy, canvas);
                        Assert.That(ground.X, Is.GreaterThan(0.1), "east of the foot");
                        sumX += ground.X;
                        count++;
                    }
                }
            }
            Assert.That(count, Is.GreaterThan(100));
            // the shadow of the axis runs from 0.5 to 1.0 tiles of height times the light's -x / z (1.13)
            double slope = -Conventions.ShadowLight.X / Conventions.ShadowLight.Z;
            Assert.That(sumX / count, Is.EqualTo(0.75 * slope).Within(0.03));
            (double footX, double footY) = FactorioCamera.Project(foot.WithZ(0), canvas);
            Assert.That(Pixels.IsShadow(result, (int)(footX * 2), (int)(footY * 2)), Is.False, "no shadow right under the foot");
        }

        [Test]
        public void MirroringSwapsTheSidesOnScreen()
        {
            PoseResult pose = TestPoses.TPose();
            foreach (bool mirrored in new[] { false, true })
            {
                CanvasSpec canvas = PlanFor(pose, 0, mirrored);
                RenderResult result = Renderer.Render(Requests.Color(pose, canvas, 0, mirrored));
                (double redX, _, int reds) = Pixels.Centroid(result.OnBlack, (x, y) => Pixels.IsReddish(result, x, y));
                (double blueX, _, int blues) = Pixels.Centroid(result.OnBlack, (x, y) => Pixels.IsBluish(result, x, y));
                Assert.That(reds, Is.GreaterThan(200));
                Assert.That(blues, Is.GreaterThan(200));
                double anchor = canvas.AnchorX * canvas.Supersampling;
                // facing north the camera sees the back, so the right side (+x, east) is on screen right
                if (mirrored)
                {
                    Assert.That(redX, Is.LessThan(anchor), "mirrored: right limbs on screen left");
                    Assert.That(blueX, Is.GreaterThan(anchor));
                }
                else
                {
                    Assert.That(redX, Is.GreaterThan(anchor), "right limbs on screen right");
                    Assert.That(blueX, Is.LessThan(anchor));
                }
            }
        }

        [Test]
        public void FacingEastMovesTheRightHandSouthAndDownTheScreen()
        {
            PoseResult pose = TestPoses.RightArm();
            Vec3 hand = pose.Joints[JointNames.Hand(1)];
            var bounds = new[] { Renderer.MeasureWorldBounds(pose, 0, false), Renderer.MeasureWorldBounds(pose, 0.25, false) };
            CanvasSpec canvas = CanvasPlanner.PlanColor(bounds, 64, 2);
            RenderResult north = Renderer.Render(Requests.Color(pose, canvas, 0));
            RenderResult east = Renderer.Render(Requests.Color(pose, canvas, 0.25));

            Vec3 handEast = Conventions.ModelPointToWorld(hand, 0.25, false);
            Assert.That(handEast.Y, Is.LessThan(-0.6), "the right hand is south");
            (double northX, double northY) = FactorioCamera.Project(Conventions.ModelPointToWorld(hand, 0, false), canvas);
            (double eastX, double eastY) = FactorioCamera.Project(handEast, canvas);
            Assert.That(eastY, Is.GreaterThan(northY + 20), "lower on screen");
            Assert.That(Pixels.IsReddish(north, (int)(northX * 2), (int)(northY * 2)), "hand drawn facing north");
            Assert.That(Pixels.IsReddish(east, (int)(eastX * 2), (int)(eastY * 2)), "hand drawn facing east");

            (_, double northCentroid, _) = Pixels.Centroid(north.OnBlack, (x, y) => Pixels.IsFigure(north, x, y));
            (_, double eastCentroid, _) = Pixels.Centroid(east.OnBlack, (x, y) => Pixels.IsFigure(east, x, y));
            Assert.That(eastCentroid, Is.GreaterThan(northCentroid + 10));
        }

        [Test]
        public void GroundToLowestPointGroundsTheFigureInBothPasses()
        {
            PoseResult floating = TestPoses.TPose(lift: 0.5);
            floating.GroundToLowestPoint = true;
            Bounds3 bounds = Renderer.MeasureWorldBounds(floating, 0.125, false);
            Assert.That(bounds.Min.Z, Is.EqualTo(0).Within(1e-12));

            PoseResult standing = TestPoses.TPose();
            standing.GroundToLowestPoint = true;
            Bounds3 reference = Renderer.MeasureWorldBounds(standing, 0.125, false);
            Assert.That(bounds.Min.ApproximatelyEquals(reference.Min, 1e-12));
            Assert.That(bounds.Max.ApproximatelyEquals(reference.Max, 1e-12));

            // the shadow pass uses the grounded figure too: its shadow equals the standing figure's
            CanvasSpec canvas = CanvasPlanner.PlanShadow(new[] { bounds }, 32, 2);
            RenderResult a = Renderer.Render(Requests.Shadow(floating, canvas, 0.125));
            RenderResult b = Renderer.Render(Requests.Shadow(standing, canvas, 0.125));
            int different = 0;
            for (int i = 0; i < a.Silhouette.Pixels.Length; i += 4)
            {
                if (a.Silhouette.Pixels[i] != b.Silhouette.Pixels[i])
                {
                    different++;
                }
            }
            Assert.That(different, Is.LessThanOrEqualTo(2), "rounding at most");
        }

        private static IEnumerable<TestCaseData> DragonCases()
        {
            // run-gun rows are (gun, move) eighths: row 4 = (1, 1) north-east, row 13 = (3, 3) south-east
            yield return new TestCaseData(AnimationSpec.RunGun, 4, 3, false).SetName("RunGunNorthEast");
            yield return new TestCaseData(AnimationSpec.RunGun, 13, 7, true).SetName("RunGunSouthEastMirrored");
            yield return new TestCaseData(AnimationSpec.Mine, 2, 8, false).SetName("MineEast");
            yield return new TestCaseData(AnimationSpec.Idle, 5, 1, false).SetName("IdleSouthWest");
        }

        [TestCaseSource(nameof(DragonCases))]
        public void ColorPassDrawsExactlyTheRayHitsInsideTheMeasuredBounds(string animation, int row, int frame, bool mirrored)
        {
            (PoseResult pose, double orientation) = TestPoses.Dragon(animation, row, frame);
            IReadOnlyList<Capsule> capsules = StickFigure.Build(pose, orientation, mirrored).Capsules;
            Bounds3 bounds = Renderer.MeasureWorldBounds(pose, orientation, mirrored).Expanded(1e-9);
            CanvasSpec canvas = CanvasPlanner.PlanColor(new[] { bounds }, 64, 2);
            RenderResult result = Renderer.Render(Requests.Color(pose, canvas, orientation, mirrored));
            int hits = 0;
            for (int y = 0; y < canvas.RenderHeight; y++)
            {
                for (int x = 0; x < canvas.RenderWidth; x++)
                {
                    (double fx, double fy) = ReferenceTracer.Centre(canvas, x, y);
                    Vec3 origin = FactorioCamera.ViewRayOrigin(fx, fy, canvas);
                    bool hit = ReferenceTracer.NearestHit(capsules, origin, out Capsule capsule, out double distance);
                    Assert.That(Pixels.IsFigure(result, x, y), Is.EqualTo(hit), $"pixel {x},{y}: culling must not change hits");
                    if (!hit)
                    {
                        continue;
                    }
                    hits++;
                    Vec3 point = origin + FactorioCamera.Forward * distance;
                    Assert.That(Inside(bounds, point), $"hit {point} outside {bounds.Min} .. {bounds.Max}");
                    (double r, double g, double b) = StickPalette.Shade(capsule.Part, capsule.Normal(point));
                    Assert.That(Pixels.Get(result.OnBlack, x, y), Is.EqualTo(((float)r, (float)g, (float)b, 1f)), "shaded nearest surface");
                }
            }
            Assert.That(hits, Is.GreaterThan(1000));
        }

        [TestCaseSource(nameof(DragonCases))]
        public void ShadowPassDrawsExactlyTheShadowOfPointsInsideTheMeasuredBounds(string animation, int row, int frame,
            bool mirrored)
        {
            (PoseResult pose, double orientation) = TestPoses.Dragon(animation, row, frame);
            IReadOnlyList<Capsule> capsules = StickFigure.Build(pose, orientation, mirrored).Capsules;
            Bounds3 bounds = Renderer.MeasureWorldBounds(pose, orientation, mirrored).Expanded(1e-9);
            CanvasSpec canvas = CanvasPlanner.PlanShadow(new[] { bounds }, 64, 2);
            RenderResult result = Renderer.Render(Requests.Shadow(pose, canvas, orientation, mirrored));
            int shadowed = 0;
            for (int y = 0; y < canvas.RenderHeight; y++)
            {
                for (int x = 0; x < canvas.RenderWidth; x++)
                {
                    (double fx, double fy) = ReferenceTracer.Centre(canvas, x, y);
                    Vec3 ground = FactorioCamera.GroundPoint(fx, fy, canvas);
                    bool inShadow = ReferenceTracer.ShadowCaster(capsules, ground, out Vec3 caster);
                    Assert.That(Pixels.IsShadow(result, x, y), Is.EqualTo(inShadow), $"pixel {x},{y}: culling must not change shadows");
                    if (inShadow)
                    {
                        shadowed++;
                        Assert.That(Inside(bounds, caster), $"caster {caster} outside {bounds.Min} .. {bounds.Max}");
                        Assert.That(PlanarShadow.Project(caster).ApproximatelyEquals(ground, 1e-9));
                    }
                }
            }
            Assert.That(shadowed, Is.GreaterThan(500));
        }

        [Test]
        public void RendersAreDeterministic()
        {
            (PoseResult pose, double orientation) = TestPoses.Dragon(AnimationSpec.RunGun, 7, 4);
            CanvasSpec canvas = PlanFor(pose, orientation);
            RenderResult a = Renderer.Render(Requests.Color(pose, canvas, orientation));
            RenderResult b = Renderer.Render(Requests.Color(pose, canvas, orientation));
            Assert.That(b.OnBlack.Pixels, Is.EqualTo(a.OnBlack.Pixels));
            Assert.That(b.OnWhite.Pixels, Is.EqualTo(a.OnWhite.Pixels));
        }

        [Test]
        public void SupersamplingRefinesTheSameImage()
        {
            PoseResult pose = TestPoses.TPose();
            CanvasSpec one = PlanFor(pose, 0.2, false, 1);
            var three = new CanvasSpec(one.Width, one.Height, one.AnchorX, one.AnchorY, one.PixelsPerTile, 3);
            ImageF coarse = Matting.FromBlackAndWhite(Renderer.Render(Requests.Color(pose, one, 0.2)).OnBlack,
                Renderer.Render(Requests.Color(pose, one, 0.2)).OnWhite, false);
            RenderResult fine = Renderer.Render(Requests.Color(pose, three, 0.2));
            Assert.That(fine.OnBlack.Width, Is.EqualTo(one.Width * 3));
            ImageF reduced = Downsample.Box(Matting.FromBlackAndWhite(fine.OnBlack, fine.OnWhite, false), 3);
            double coarseCoverage = 0, fineCoverage = 0;
            for (int i = 3; i < coarse.Pixels.Length; i += 4)
            {
                coarseCoverage += coarse.Pixels[i];
                fineCoverage += reduced.Pixels[i];
            }
            Assert.That(fineCoverage, Is.EqualTo(coarseCoverage).Within(coarseCoverage * 0.05), "same area");
        }

        [Test]
        public void TypicalFrameRendersFast()
        {
            (PoseResult pose, double orientation) = TestPoses.Dragon(AnimationSpec.RunGun, 2, 5);
            Assert.That(StickFigure.Build(pose, orientation, false).Capsules.Count, Is.EqualTo(23));
            var canvas = new CanvasSpec(224, 256, 112, 200, 64, 2);
            Renderer.Render(Requests.Color(pose, canvas, orientation)); // warm up
            Renderer.Render(Requests.Shadow(pose, canvas, orientation));
            double best = double.PositiveInfinity;
            for (int i = 0; i < 3; i++)
            {
                var watch = Stopwatch.StartNew();
                Renderer.Render(Requests.Color(pose, canvas, orientation));
                Renderer.Render(Requests.Shadow(pose, canvas, orientation));
                best = Math.Min(best, watch.Elapsed.TotalSeconds);
            }
            Assert.That(best, Is.LessThan(0.5), "a color and a shadow pass");
        }

        [Test]
        public void RejectsBadRequests()
        {
            PoseResult pose = TestPoses.TPose();
            var canvas = new CanvasSpec(64, 64, 32, 60, 32, 1);
            Assert.That(() => Renderer.Render(null), Throws.ArgumentNullException);
            FrameRequest noPose = Requests.Color(pose, canvas);
            noPose.Pose = null;
            Assert.That(() => Renderer.Render(noPose), Throws.ArgumentException.With.Message.Contains("no pose"));
            FrameRequest noCanvas = Requests.Color(pose, default);
            Assert.That(() => Renderer.Render(noCanvas), Throws.ArgumentException.With.Message.Contains("canvas"));
            FrameRequest badPass = Requests.Color(pose, canvas);
            badPass.Pass = (RenderPass)7;
            Assert.That(() => Renderer.Render(badPass), Throws.ArgumentException.With.Message.Contains("pass"));
            FrameRequest badOrientation = Requests.Color(pose, canvas, double.NaN);
            Assert.That(() => Renderer.Render(badOrientation), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => Renderer.MeasureWorldBounds(null, 0, false), Throws.ArgumentNullException);
        }

        [Test]
        public void EmptyPosesRenderEmptyImages()
        {
            var canvas = new CanvasSpec(16, 16, 8, 14, 32, 2);
            RenderResult color = Renderer.Render(Requests.Color(new PoseResult(), canvas));
            Assert.That(color.OnBlack.Pixels.Where((v, i) => i % 4 != 3), Is.All.EqualTo(0f));
            Assert.That(color.OnWhite.Pixels, Is.All.EqualTo(1f));
            RenderResult shadow = Renderer.Render(Requests.Shadow(new PoseResult(), canvas));
            Assert.That(shadow.Silhouette.Pixels, Is.All.EqualTo(1f));
            Assert.That(Renderer.MeasureWorldBounds(new PoseResult(), 0, false).IsEmpty, Is.True);
        }

        private static bool Inside(Bounds3 b, Vec3 p) =>
            p.X >= b.Min.X && p.X <= b.Max.X && p.Y >= b.Min.Y && p.Y <= b.Max.Y && p.Z >= b.Min.Z && p.Z <= b.Max.Z;

        /// <summary>Distance between the line o + t d (unit d) and the segment a - b, by golden section on the segment.</summary>
        private static double LineToSegmentDistance(Vec3 o, Vec3 d, Vec3 a, Vec3 b)
        {
            double Distance(double u)
            {
                Vec3 p = Vec3.Lerp(a, b, u) - o;
                return (p - d * Vec3.Dot(p, d)).Length;
            }
            // the distance is convex in u
            double lo = 0, hi = 1;
            for (int i = 0; i < 200; i++)
            {
                double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
                if (Distance(m1) < Distance(m2))
                {
                    hi = m2;
                }
                else
                {
                    lo = m1;
                }
            }
            return Distance((lo + hi) / 2);
        }
    }
}

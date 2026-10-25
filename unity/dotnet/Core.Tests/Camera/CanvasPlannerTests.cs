using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Camera
{
    public class CanvasPlannerTests
    {
        private static Bounds3 RandomBox(Random rng)
        {
            var a = new Vec3(rng.NextDouble() * 4 - 2, rng.NextDouble() * 4 - 2, rng.NextDouble() * 2.5 - 0.3);
            var size = new Vec3(rng.NextDouble(), rng.NextDouble(), rng.NextDouble() * 1.5);
            return new Bounds3(a, a + size);
        }

        private static List<Bounds3> RandomBoxes(Random rng) =>
            Enumerable.Range(0, rng.Next(1, 6)).Select(_ => RandomBox(rng)).ToList();

        /// <summary>
        /// Every point lands in pixels margin .. size - margin - 1, and the plan is tight: some point is in the
        /// first allowed pixel and some in the last, on both axes.
        /// </summary>
        private static void AssertEnclosesTightly(CanvasSpec canvas, IEnumerable<Vec3> points, int margin)
        {
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
            foreach (Vec3 p in points)
            {
                (double x, double y) = FactorioCamera.Project(p, canvas);
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
            // integer anchors make the projected offsets exact enough for a tiny tolerance
            const double tol = 1e-9;
            Assert.That(minX, Is.GreaterThanOrEqualTo(margin - tol), "left margin");
            Assert.That(minY, Is.GreaterThanOrEqualTo(margin - tol), "top margin");
            Assert.That(maxX, Is.LessThan(canvas.Width - margin), "right margin");
            Assert.That(maxY, Is.LessThan(canvas.Height - margin), "bottom margin");
            Assert.That(minX, Is.LessThan(margin + 1 + tol), "no spare column on the left");
            Assert.That(minY, Is.LessThan(margin + 1 + tol), "no spare row at the top");
            Assert.That(maxX, Is.GreaterThanOrEqualTo(canvas.Width - margin - 1 - tol), "no spare column on the right");
            Assert.That(maxY, Is.GreaterThanOrEqualTo(canvas.Height - margin - 1 - tol), "no spare row at the bottom");
        }

        [Test]
        public void ColorCanvasEnclosesEveryCornerWithTheMargin()
        {
            var rng = new Random(321);
            for (int i = 0; i < 300; i++)
            {
                List<Bounds3> boxes = RandomBoxes(rng);
                int ppt = rng.Next(0, 2) == 0 ? 32 : 64;
                int margin = rng.Next(0, 9);
                CanvasSpec canvas = CanvasPlanner.PlanColor(boxes, ppt, 2, margin);
                Assert.That(canvas.PixelsPerTile, Is.EqualTo(ppt));
                Assert.That(canvas.Supersampling, Is.EqualTo(2));
                AssertEnclosesTightly(canvas, boxes.SelectMany(b => b.Corners()), margin);
            }
        }

        [Test]
        public void ShadowCanvasEnclosesTheShadowOfEveryCorner()
        {
            var rng = new Random(322);
            for (int i = 0; i < 300; i++)
            {
                List<Bounds3> boxes = RandomBoxes(rng);
                int margin = rng.Next(0, 9);
                CanvasSpec canvas = CanvasPlanner.PlanShadow(boxes, 64, 3, margin);
                Assert.That(canvas.Supersampling, Is.EqualTo(3));
                AssertEnclosesTightly(canvas, boxes.SelectMany(b => b.Corners()).Select(PlanarShadow.Project), margin);
            }
        }

        [Test]
        public void ShadowOfAnyPointInsideTheBoxesFits()
        {
            var rng = new Random(323);
            for (int i = 0; i < 50; i++)
            {
                List<Bounds3> boxes = RandomBoxes(rng);
                CanvasSpec canvas = CanvasPlanner.PlanShadow(boxes, 64, 1);
                foreach (Bounds3 box in boxes)
                {
                    for (int k = 0; k < 20; k++)
                    {
                        var p = new Vec3(MathUtil.Lerp(box.Min.X, box.Max.X, rng.NextDouble()),
                            MathUtil.Lerp(box.Min.Y, box.Max.Y, rng.NextDouble()),
                            MathUtil.Lerp(box.Min.Z, box.Max.Z, rng.NextDouble()));
                        (double x, double y) = FactorioCamera.Project(PlanarShadow.Project(p), canvas);
                        Assert.That(x, Is.InRange(CanvasPlanner.DefaultMarginPixels, canvas.Width - CanvasPlanner.DefaultMarginPixels));
                        Assert.That(y, Is.InRange(CanvasPlanner.DefaultMarginPixels, canvas.Height - CanvasPlanner.DefaultMarginPixels));
                    }
                }
            }
        }

        [Test]
        public void HandComputedCanvas()
        {
            // one point at the origin and one 1 tile east, 1 tile up: offsets x in [0, 64], y in [-56.57, 0]
            var bounds = new[] { new Bounds3(Vec3.Zero, Vec3.Zero), new Bounds3(new Vec3(1, 0, 1), new Vec3(1, 0, 1)) };
            CanvasSpec canvas = CanvasPlanner.PlanColor(bounds, 64, 2);
            Assert.That(canvas.AnchorX, Is.EqualTo(4));
            Assert.That(canvas.Width, Is.EqualTo(4 + 64 + 1 + 4));
            Assert.That(canvas.AnchorY, Is.EqualTo(4 + 57));
            Assert.That(canvas.Height, Is.EqualTo(61 + 0 + 1 + 4));
            Assert.That(canvas.PixelsPerTile, Is.EqualTo(64));
            Assert.That(canvas.Supersampling, Is.EqualTo(2));
        }

        [Test]
        public void TheAnchorMayLieOutsideTheCanvas()
        {
            var far = new Bounds3(new Vec3(5, 0, 0), new Vec3(6, 0, 0));
            CanvasSpec canvas = CanvasPlanner.PlanColor(new[] { far }, 64, 1, 0);
            Assert.That(canvas.AnchorX, Is.EqualTo(-320));
            Assert.That(canvas.Width, Is.EqualTo(65));
        }

        [Test]
        public void PlansAreDeterministicAndIgnoreOrderAndEmptyBoxes()
        {
            var rng = new Random(324);
            for (int i = 0; i < 50; i++)
            {
                List<Bounds3> boxes = RandomBoxes(rng);
                CanvasSpec a = CanvasPlanner.PlanColor(boxes, 64, 2);
                var shuffled = boxes.OrderBy(_ => rng.Next()).ToList();
                shuffled.Insert(rng.Next(shuffled.Count + 1), Bounds3.Empty);
                CanvasSpec b = CanvasPlanner.PlanColor(shuffled, 64, 2);
                Assert.That(b, Is.EqualTo(a));
                Assert.That(CanvasPlanner.PlanShadow(shuffled, 64, 2), Is.EqualTo(CanvasPlanner.PlanShadow(boxes, 64, 2)));
            }
        }

        [Test]
        public void RejectsBadArguments()
        {
            var box = new[] { new Bounds3(Vec3.Zero, new Vec3(1, 1, 1)) };
            Assert.That(() => CanvasPlanner.PlanColor(null, 64, 2), Throws.ArgumentNullException);
            Assert.That(() => CanvasPlanner.PlanColor(box, 0, 2), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => CanvasPlanner.PlanColor(box, 64, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => CanvasPlanner.PlanShadow(box, 64, 2, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => CanvasPlanner.PlanColor(Array.Empty<Bounds3>(), 64, 2),
                Throws.ArgumentException.With.Message.Contains("no non-empty bounds"));
            Assert.That(() => CanvasPlanner.PlanShadow(new[] { Bounds3.Empty }, 64, 2), Throws.ArgumentException);
            var infinite = new Bounds3(Vec3.Zero, new Vec3(double.PositiveInfinity, 1, 1));
            Assert.That(() => CanvasPlanner.PlanColor(new[] { infinite }, 64, 2),
                Throws.ArgumentException.With.Message.Contains("finite"));
            var nan = new Bounds3(new Vec3(double.NaN, 0, 0), new Vec3(1, 1, 1));
            Assert.That(() => CanvasPlanner.PlanColor(new[] { nan }, 64, 2), Throws.ArgumentException);
            var huge = new Bounds3(Vec3.Zero, new Vec3(300, 0, 0));
            Assert.That(() => CanvasPlanner.PlanColor(new[] { huge }, 64, 2),
                Throws.ArgumentException.With.Message.Contains("at most"));
        }
    }
}

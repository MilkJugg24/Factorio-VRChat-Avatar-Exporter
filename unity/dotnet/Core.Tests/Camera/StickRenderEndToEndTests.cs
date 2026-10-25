using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Camera
{
    /// <summary>
    /// Dragon poses through the whole frame path the exporter will use: measure, plan the canvases, render,
    /// matte (or turn the silhouette into a shadow layer) and quantize.
    /// </summary>
    public class StickRenderEndToEndTests
    {
        private const int Supersampling = 2;
        private static readonly StickFigureRenderer Renderer = new StickFigureRenderer();
        private static readonly DragonPoseSource Source = new DragonPoseSource(BodyShape.Male);

        private sealed class Frame
        {
            public int Row;
            public int Index;
            public PoseResult Pose;
            public double Orientation;
        }

        private static List<Frame> Frames(string animation)
        {
            AnimationInfo info = AnimationSpec.Get(animation);
            var frames = new List<Frame>();
            for (int row = 0; row < info.DirectionCount; row++)
            {
                DirectionInfo direction = AnimationSpec.Direction(animation, row);
                foreach (int index in new[] { 0, info.FrameCount / 2 })
                {
                    frames.Add(new Frame
                    {
                        Row = row,
                        Index = index,
                        Pose = Source.Pose(animation, AnimationSpec.FramePhase(animation, index), direction.Move),
                        Orientation = direction.Orientation,
                    });
                }
            }
            return frames;
        }

        private static FrameRequest Request(string animation, Frame frame, RenderPass pass, CanvasSpec canvas, bool mirrored) =>
            new FrameRequest
            {
                Animation = animation,
                Direction = frame.Row,
                Frame = frame.Index,
                Pose = frame.Pose,
                Orientation = frame.Orientation,
                Mirrored = mirrored,
                Pass = pass,
                Canvas = canvas,
            };

        [TestCase(AnimationSpec.Idle, false)]
        [TestCase(AnimationSpec.RunGun, false)]
        [TestCase(AnimationSpec.RunGun, true)]
        public void DragonFramesFitTheirPlannedCanvases(string animation, bool mirrored)
        {
            List<Frame> frames = Frames(animation);
            List<Bounds3> bounds = frames.Select(f => Renderer.MeasureWorldBounds(f.Pose, f.Orientation, mirrored)).ToList();
            CanvasSpec color = CanvasPlanner.PlanColor(bounds, Conventions.DefaultPixelsPerTile, Supersampling);
            CanvasSpec shadow = CanvasPlanner.PlanShadow(bounds, Conventions.DefaultPixelsPerTile, Supersampling);
            // a 1.5 tile character makes a sprite of roughly vanilla size
            Assert.That(color.Width, Is.InRange(30, 200), "color width");
            Assert.That(color.Height, Is.InRange(80, 220), "color height");
            Assert.That(shadow.Width, Is.InRange(60, 260), "shadow width");
            Assert.That(shadow.Height, Is.InRange(20, 200), "shadow height");

            foreach (Frame frame in frames)
            {
                string where = $"{animation} row {frame.Row} frame {frame.Index}";
                RenderResult render = Renderer.Render(Request(animation, frame, RenderPass.Color, color, mirrored));
                ImageF matte = Matting.FromBlackAndWhite(render.OnBlack, render.OnWhite, render.Linear);
                Image8 sprite = ImageConvert.ToImage8(Downsample.Box(matte, Supersampling));
                Assert.That(sprite.Width, Is.EqualTo(color.Width));
                (int covered, bool touchesMargin) = Pixels.Coverage(sprite, CanvasPlanner.DefaultMarginPixels);
                Assert.That(covered, Is.GreaterThan(400), where + ": the figure is drawn");
                Assert.That(touchesMargin, Is.False, where + ": the figure stays inside the margin");

                RenderResult silhouette = Renderer.Render(Request(animation, frame, RenderPass.Shadow, shadow, mirrored));
                Image8 layer = ImageConvert.ShadowFromSilhouette(silhouette.Silhouette, Supersampling, silhouette.Linear);
                Assert.That(layer.Width, Is.EqualTo(shadow.Width));
                (int shaded, bool shadowTouchesMargin) = Pixels.Coverage(layer, CanvasPlanner.DefaultMarginPixels);
                Assert.That(shaded, Is.GreaterThan(300), where + ": the shadow is drawn");
                Assert.That(shadowTouchesMargin, Is.False, where + ": the shadow stays inside the margin");
            }
        }

        [Test]
        public void GunRowsShowTheGun()
        {
            // row 7 aims east, across the screen, with the gun on the camera's side of the body
            Frame frame = Frames(AnimationSpec.RunGun).First(f => f.Row == 7);
            CanvasSpec canvas = CanvasPlanner.PlanColor(
                new[] { Renderer.MeasureWorldBounds(frame.Pose, frame.Orientation, false) }, 64, Supersampling);
            RenderResult render = Renderer.Render(Request(AnimationSpec.RunGun, frame, RenderPass.Color, canvas, false));
            (double r, double g, double b) prop = StickPalette.Albedo(StickPart.Prop);
            (_, _, int dark) = Pixels.Centroid(render.OnBlack, (x, y) =>
            {
                (float pr, float pg, float pb, _) = Pixels.Get(render.OnBlack, x, y);
                bool figure = pr + pg + pb > 0;
                return figure && pr <= prop.r + 1e-6 && pg <= prop.g + 1e-6 && pb <= prop.b + 1e-6;
            });
            Assert.That(dark, Is.GreaterThan(30), "prop colored pixels");
        }
    }
}

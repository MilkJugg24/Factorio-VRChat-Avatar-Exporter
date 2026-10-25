using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Export
{
    /// <summary>
    /// Checks that the exporter gives every render its own pose, orientation and canvas, and builds each layer's
    /// sheets and the pack art from the right frames and anchors.
    /// </summary>
    public class AvatarExporterWiringTests
    {
        /// <summary>The stick figure renderer, recording every request.</summary>
        private sealed class RecordingStickRenderer : IFrameRenderer
        {
            private readonly StickFigureRenderer inner = new StickFigureRenderer();
            public readonly List<FrameRequest> Requests = new List<FrameRequest>();

            public Bounds3 MeasureWorldBounds(PoseResult pose, double orientation, bool mirrored) =>
                inner.MeasureWorldBounds(pose, orientation, mirrored);

            public RenderResult Render(FrameRequest request)
            {
                Requests.Add(request);
                return inner.Render(request);
            }
        }

        /// <summary>
        /// Draws a bar 6 px wide straight up from the entity position to the height of the head top (supersampling
        /// 1): blue below the pelvis, and above it a color that tells rows and frames apart, red 40 + 10 * row and
        /// green 40 + 10 * frame. No shadows.
        /// </summary>
        private sealed class PortraitRenderer : IFrameRenderer
        {
            public Bounds3 MeasureWorldBounds(PoseResult pose, double orientation, bool mirrored) =>
                new Bounds3(new Vec3(-0.3, -0.3, 0), new Vec3(0.3, 0.3, 2));

            public RenderResult Render(FrameRequest request)
            {
                CanvasSpec c = request.Canvas;
                var black = new ImageF(c.Width, c.Height);
                black.Fill(0, 0, 0, 1);
                var white = new ImageF(c.Width, c.Height);
                white.Fill(1, 1, 1, 1);
                if (request.Pass == RenderPass.Shadow)
                {
                    return new RenderResult { Silhouette = white };
                }
                double top = Row(request.Pose.Joints[JointNames.HeadTop].Z, c);
                double pelvis = Row(request.Pose.Joints[JointNames.Pelvis].Z, c);
                for (int y = (int)top; y < c.AnchorY; y++)
                {
                    bool legs = y >= pelvis;
                    float r = legs ? 0 : (40 + 10 * request.Direction) / 255f;
                    float g = legs ? 0 : (40 + 10 * request.Frame) / 255f;
                    float b = legs ? 1 : 0;
                    for (int x = c.AnchorX - 3; x < c.AnchorX + 3; x++)
                    {
                        foreach (ImageF image in new[] { black, white })
                        {
                            image[x, y, 0] = r;
                            image[x, y, 1] = g;
                            image[x, y, 2] = b;
                        }
                    }
                }
                return new RenderResult { OnBlack = black, OnWhite = white };
            }

            /// <summary>The canvas row of the point at height z above the entity position.</summary>
            private static double Row(double z, CanvasSpec c) => FactorioCamera.Project(new Vec3(0, 0, z), c).y;
        }

        private ExportSettings stickSettings;
        private HumanoidAnimations poses;
        private RecordingStickRenderer stick;

        /// <summary>One export of the generic rig with the stick figure renderer, and a margin that is not the default.</summary>
        [OneTimeSetUp]
        public void ExportTheGenericRig()
        {
            stickSettings = new ExportSettings { PixelsPerTile = 32, Supersampling = 1, Flight = true, CanvasMarginPixels = 2 };
            poses = new HumanoidAnimations(HumanoidRig.Measure(SyntheticRigs.Generic(1.6)));
            stick = new RecordingStickRenderer();
            new AvatarExporter(poses, stick, stickSettings).Render();
        }

        private static double MirroredOrientation(double orientation) => MathUtil.PositiveModulo(1 - orientation, 1);

        [Test]
        public void EveryRequestHasItsFramesPoseAndOrientation()
        {
            Assert.That(stick.Requests.Count, Is.EqualTo(new AvatarExporter(poses, stick, stickSettings).TotalRenders));
            var expectedPoses = new Dictionary<(string, int, int), PoseResult>();
            foreach (FrameRequest r in stick.Requests)
            {
                string what = $"{r.Animation} direction {r.Direction} frame {r.Frame} {r.Pass} mirrored {r.Mirrored}";
                DirectionInfo direction = AnimationSpec.Direction(r.Animation, r.Direction);
                double orientation = r.Mirrored ? MirroredOrientation(direction.Orientation) : direction.Orientation;
                Assert.That(r.Orientation, Is.EqualTo(orientation).Within(1e-12), what);
                if (!expectedPoses.TryGetValue((r.Animation, r.Direction, r.Frame), out PoseResult expected))
                {
                    expected = poses.Pose(r.Animation, AnimationSpec.FramePhase(r.Animation, r.Frame), direction.Move);
                    expectedPoses.Add((r.Animation, r.Direction, r.Frame), expected);
                }
                Assert.That(r.Pose.Joints.Keys, Is.EquivalentTo(expected.Joints.Keys), what);
                Assert.That(r.Pose.Joints.All(j => j.Value == expected.Joints[j.Key]), what + ": joints");
            }
        }

        [Test]
        public void EachLayerIsPlannedFromTheBoundsOfItsFrames()
        {
            ExportSettings s = stickSettings;
            foreach (AnimationInfo a in AnimationSpec.ForPack(s.Flight))
            {
                var bounds = new List<Bounds3>();
                var mirroredBounds = new List<Bounds3>();
                for (int d = 0; d < a.DirectionCount; d++)
                {
                    DirectionInfo direction = AnimationSpec.Direction(a.Name, d);
                    for (int f = 0; f < a.FrameCount; f++)
                    {
                        PoseResult pose = poses.Pose(a.Name, AnimationSpec.FramePhase(a.Name, f), direction.Move);
                        bounds.Add(stick.MeasureWorldBounds(pose, direction.Orientation, false));
                        mirroredBounds.Add(stick.MeasureWorldBounds(pose, MirroredOrientation(direction.Orientation), true));
                    }
                }
                var expected = new Dictionary<(RenderPass, bool), CanvasSpec>
                {
                    [(RenderPass.Color, false)] = CanvasPlanner.PlanColor(bounds, s.PixelsPerTile, s.Supersampling, s.CanvasMarginPixels),
                    [(RenderPass.Shadow, false)] = CanvasPlanner.PlanShadow(bounds, s.PixelsPerTile, s.Supersampling, s.CanvasMarginPixels),
                    [(RenderPass.Shadow, true)] = CanvasPlanner.PlanShadow(mirroredBounds, s.PixelsPerTile, s.Supersampling, s.CanvasMarginPixels),
                };
                foreach (FrameRequest r in stick.Requests.Where(r => r.Animation == a.Name))
                {
                    Assert.That(Describe(r.Canvas), Is.EqualTo(Describe(expected[(r.Pass, r.Mirrored)])),
                        $"{a.Name} {r.Pass} mirrored {r.Mirrored}");
                }
            }
        }

        private static (int, int, int, int, int, int) Describe(CanvasSpec c) =>
            (c.Width, c.Height, c.AnchorX, c.AnchorY, c.PixelsPerTile, c.Supersampling);

        [Test]
        public void EveryLayerPlacesTheMarkAtTheSameSpot()
        {
            // the recording renderer marks the entity position with a 4x4 px square above it in every pass
            var renderer = new AvatarExporterTests.RecordingRenderer();
            var settings = new ExportSettings { PixelsPerTile = 32, Supersampling = 1, Flight = true };
            ExportedArt art = new AvatarExporter(new DragonPoseSource(BodyShape.Male), renderer, settings).Render();
            foreach (KeyValuePair<string, Dictionary<string, SheetSet>> animation in art.Animations)
            {
                foreach (KeyValuePair<string, SheetSet> layer in animation.Value)
                {
                    (double x, double y) = MarkPosition(layer.Value, settings.PixelsPerTile);
                    Assert.That(x, Is.EqualTo(0).Within(1e-4), animation.Key + " " + layer.Key);
                    Assert.That(y, Is.EqualTo(-2.0 / 32).Within(1e-4), animation.Key + " " + layer.Key);
                }
            }
        }

        /// <summary>
        /// Where the centre of the drawn pixels of the first frame lands relative to the entity position, tiles:
        /// Factorio draws a frame centred on the entity position plus the shift.
        /// </summary>
        private static (double x, double y) MarkPosition(SheetSet sheets, int pixelsPerTile)
        {
            Image8 sheet = sheets.Files[0];
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = 0; y < sheets.FrameHeight; y++)
            {
                for (int x = 0; x < sheets.FrameWidth; x++)
                {
                    if (sheet.Alpha(x, y) > 0)
                    {
                        x0 = Math.Min(x0, x);
                        y0 = Math.Min(y0, y);
                        x1 = Math.Max(x1, x + 1);
                        y1 = Math.Max(y1, y + 1);
                    }
                }
            }
            Assert.That(x1, Is.GreaterThan(0), "nothing drawn");
            return (((x0 + x1) / 2.0 - sheets.FrameWidth / 2.0) / pixelsPerTile + sheets.ShiftX,
                ((y0 + y1) / 2.0 - sheets.FrameHeight / 2.0) / pixelsPerTile + sheets.ShiftY);
        }

        [Test]
        public void IconAndThumbnailShowTheHeadAndShouldersOfTheFirstFrameFacingSouth()
        {
            var settings = new ExportSettings { PixelsPerTile = 32, Supersampling = 1, Flight = false };
            ExportedArt art = new AvatarExporter(poses, new PortraitRenderer(), settings).Render();
            // south is row 4, so the body above the pelvis is (80, 40, 0)
            (byte, byte, byte) south = (80, 40, 0);
            Image8 icon = art.Icon;
            int opaque = 0;
            for (int y = 0; y < icon.Height; y++)
            {
                for (int x = 0; x < icon.Width; x++)
                {
                    Assert.That(icon[x, y, 2], Is.EqualTo(0), $"no legs in the icon, pixel {x},{y}");
                    if (icon.Alpha(x, y) == 255)
                    {
                        Assert.That((icon[x, y, 0], icon[x, y, 1], icon[x, y, 2]), Is.EqualTo(south), $"icon pixel {x},{y}");
                        opaque++;
                    }
                }
            }
            Assert.That(opaque, Is.GreaterThan(0));
            Assert.That(icon.Alpha(2, 32) == 0 && icon.Alpha(61, 32) == 0, "the square reaches down to the chest, so it is wider than the bar");
            Image8 thumbnail = art.Thumbnail;
            Assert.That((thumbnail[72, 20, 0], thumbnail[72, 20, 1], thumbnail[72, 20, 2]), Is.EqualTo(south), "thumbnail");
        }

        [Test]
        public void SheetsAreSplitAtTheLargestSheetSize()
        {
            // the recording renderer's frames are 6x6 px: the 18 rows of run-gun take 108 px, and 80 px hold 13
            var settings = new ExportSettings { PixelsPerTile = 32, Supersampling = 1, Flight = false, MaxSheetSize = 80 };
            ExportedArt art = new AvatarExporter(new DragonPoseSource(BodyShape.Male), new AvatarExporterTests.RecordingRenderer(),
                settings).Render();
            SheetSet runGun = art.Animations[AnimationSpec.RunGun][PackLayers.Base];
            Assert.That((runGun.FrameWidth, runGun.FrameHeight), Is.EqualTo((6, 6)));
            Assert.That(runGun.LinesPerFile, Is.EqualTo(13));
            Assert.That(runGun.Files.Count, Is.EqualTo(2));
            Assert.That(art.Animations[AnimationSpec.Idle][PackLayers.Base].LinesPerFile, Is.Null);
        }
    }
}

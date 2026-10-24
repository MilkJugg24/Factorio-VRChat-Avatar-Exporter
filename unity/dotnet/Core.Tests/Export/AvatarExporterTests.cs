using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using MilkJugg24.FactorioAvatarExporter.Core.Tests.Pack;

namespace MilkJugg24.FactorioAvatarExporter.Core.Tests.Export
{
    public class AvatarExporterTests
    {
        /// <summary>
        /// Draws an opaque red square around the anchor (and a black one on the shadow pass, plus its top left
        /// pixel with <see cref="ShadowInCorner"/>) and records every request.
        /// </summary>
        internal sealed class RecordingRenderer : IFrameRenderer
        {
            public readonly List<FrameRequest> Requests = new List<FrameRequest>();
            public int WrongSizeAfter = int.MaxValue;
            public float ShadowValue;
            public bool ShadowInCorner;
            public bool Linear;

            public Bounds3 MeasureWorldBounds(PoseResult pose, double orientation, bool mirrored) =>
                new Bounds3(new Vec3(-0.3, -0.3, 0), new Vec3(0.3, 0.3, 1.5));

            public RenderResult Render(FrameRequest request)
            {
                Requests.Add(request);
                CanvasSpec c = request.Canvas;
                int w = Requests.Count > WrongSizeAfter ? c.RenderWidth + 1 : c.RenderWidth;
                if (request.Pass == RenderPass.Shadow)
                {
                    var silhouette = new ImageF(w, c.RenderHeight);
                    silhouette.Fill(1, 1, 1, 1);
                    Square(silhouette, c, ShadowValue, ShadowValue, ShadowValue);
                    if (ShadowInCorner)
                    {
                        silhouette[0, 0, 0] = 0;
                        silhouette[0, 0, 1] = 0;
                        silhouette[0, 0, 2] = 0;
                    }
                    return new RenderResult { Silhouette = silhouette, Linear = Linear };
                }
                var black = new ImageF(w, c.RenderHeight);
                black.Fill(0, 0, 0, 1);
                var white = new ImageF(w, c.RenderHeight);
                white.Fill(1, 1, 1, 1);
                Square(black, c, 1, 0, 0);
                Square(white, c, 1, 0, 0);
                return new RenderResult { OnBlack = black, OnWhite = white, Linear = Linear };
            }

            private static void Square(ImageF image, CanvasSpec c, float r, float g, float b)
            {
                int ax = Math.Clamp(c.AnchorX * c.Supersampling, 4, image.Width - 4);
                int ay = Math.Clamp(c.AnchorY * c.Supersampling, 4, image.Height - 4);
                for (int y = ay - 4; y < ay; y++)
                {
                    for (int x = ax - 2; x < ax + 2; x++)
                    {
                        image[x, y, 0] = r;
                        image[x, y, 1] = g;
                        image[x, y, 2] = b;
                    }
                }
            }
        }

        /// <summary>The stick figure renderer, but its bounds leave out everything within 0.15 tiles of their sides.</summary>
        private sealed class ShrunkBoundsRenderer : IFrameRenderer
        {
            private readonly StickFigureRenderer inner = new StickFigureRenderer();

            public Bounds3 MeasureWorldBounds(PoseResult pose, double orientation, bool mirrored) =>
                inner.MeasureWorldBounds(pose, orientation, mirrored).Expanded(-0.15);

            public RenderResult Render(FrameRequest request) => inner.Render(request);
        }

        /// <summary>
        /// Fills the whole color pass with a half transparent grey (0.2 on black, 0.7 on white), blackens every
        /// other column of the shadow pass, and records every request. Its bounds reach
        /// <see cref="HalfWidth"/> tiles east and west.
        /// </summary>
        private sealed class GreyRenderer : IFrameRenderer
        {
            public readonly List<FrameRequest> Requests = new List<FrameRequest>();
            public bool Linear;
            public double HalfWidth = 0.3;

            public Bounds3 MeasureWorldBounds(PoseResult pose, double orientation, bool mirrored) =>
                new Bounds3(new Vec3(-HalfWidth, -0.3, 0), new Vec3(HalfWidth, 0.3, 1.5));

            public RenderResult Render(FrameRequest request)
            {
                Requests.Add(request);
                CanvasSpec c = request.Canvas;
                if (request.Pass == RenderPass.Shadow)
                {
                    var silhouette = new ImageF(c.RenderWidth, c.RenderHeight);
                    silhouette.Fill(1, 1, 1, 1);
                    for (int y = 0; y < silhouette.Height; y++)
                    {
                        for (int x = 0; x < silhouette.Width; x += 2)
                        {
                            silhouette[x, y, 0] = 0;
                            silhouette[x, y, 1] = 0;
                            silhouette[x, y, 2] = 0;
                        }
                    }
                    return new RenderResult { Silhouette = silhouette, Linear = Linear };
                }
                var black = new ImageF(c.RenderWidth, c.RenderHeight);
                black.Fill(0.2f, 0.2f, 0.2f, 1);
                var white = new ImageF(c.RenderWidth, c.RenderHeight);
                white.Fill(0.7f, 0.7f, 0.7f, 1);
                return new RenderResult { OnBlack = black, OnWhite = white, Linear = Linear };
            }
        }

        private static ExportSettings Small(bool flight = false) =>
            new ExportSettings { PixelsPerTile = 32, Supersampling = 1, Flight = flight };

        private static HumanoidAnimations GenericRig() =>
            new HumanoidAnimations(HumanoidRig.Measure(SyntheticRigs.Generic(1.6)));

        [Test]
        public void RendersEveryPassOfEveryFrame()
        {
            var renderer = new RecordingRenderer();
            var exporter = new AvatarExporter(new DragonPoseSource(BodyShape.Male), renderer, Small(true));
            var reports = new List<ExportProgress>();
            ExportedArt art = exporter.Render(new ListProgress(reports));

            Assert.That(renderer.Requests.Count, Is.EqualTo(exporter.TotalRenders));
            Assert.That(reports.Count, Is.EqualTo(exporter.TotalRenders));
            Assert.That(reports.Last().Done, Is.EqualTo(reports.Last().Total));
            Assert.That(art.Animations.Keys, Is.EquivalentTo(AnimationSpec.All.Select(a => a.Name)));
            foreach (AnimationInfo a in AnimationSpec.All)
            {
                Dictionary<string, SheetSet> layers = art.Animations[a.Name];
                string[] expected = a.Name == AnimationSpec.RunGun
                    ? new[] { PackLayers.Base, PackLayers.Shadow, PackLayers.ShadowFlipped }
                    : new[] { PackLayers.Base, PackLayers.Shadow };
                Assert.That(layers.Keys, Is.EquivalentTo(expected), a.Name);
                Assert.That(layers[PackLayers.Base].DirectionCount, Is.EqualTo(a.DirectionCount));
                Assert.That(layers[PackLayers.Base].FrameCount, Is.EqualTo(a.FrameCount));
            }
            Assert.That(art.Icon.Width, Is.EqualTo(PackArt.IconSize));
            Assert.That(art.Thumbnail.Width, Is.EqualTo(PackFormat.ThumbnailSize));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShadowPassesFollowTheRenderersColorSpace(bool linear)
        {
            // a 60% shadow: linear 0.4, or the same grey stored as sRGB
            float grey = linear ? 0.4f : (float)ColorSpace.LinearToSrgb(0.4);
            var renderer = new RecordingRenderer { ShadowValue = grey, Linear = linear };
            ExportedArt art = new AvatarExporter(new DragonPoseSource(BodyShape.Male), renderer, Small()).Render();
            Image8 sheet = art.Animations[AnimationSpec.Idle][PackLayers.Shadow].Files[0];
            byte darkest = 0;
            for (int i = 3; i < sheet.Pixels.Length; i += 4)
            {
                darkest = Math.Max(darkest, sheet.Pixels[i]);
            }
            Assert.That(darkest, Is.EqualTo(153));
        }

        [TestCase(false, 67, 149)]
        [TestCase(true, 170, 128)]
        public void SupersampledRendersAreMattedAndReducedToTheCanvas(bool linear, int grey, int alpha)
        {
            // Linear: alpha = 1 - (0.7 - 0.2) = 0.5 and color 0.2 / 0.5 = 0.4, sRGB 170. sRGB: 0.2 and 0.7
            // decode to 0.0331 and 0.4479, so alpha = 0.585 (149) and color 0.0566, sRGB 67. The renders fill
            // their canvases, so frames are cut to the whole canvas, and there is no margin to keep empty.
            var settings = new ExportSettings { PixelsPerTile = 32, Supersampling = 2, Flight = false, CanvasMarginPixels = 0 };
            var renderer = new GreyRenderer { Linear = linear };
            ExportedArt art = new AvatarExporter(new DragonPoseSource(BodyShape.Male), renderer, settings).Render();
            CanvasSpec color = renderer.Requests.First(r => r.Animation == AnimationSpec.Idle && r.Pass == RenderPass.Color).Canvas;
            CanvasSpec shadow = renderer.Requests.First(r => r.Animation == AnimationSpec.Idle && r.Pass == RenderPass.Shadow).Canvas;
            SheetSet baseSheets = art.Animations[AnimationSpec.Idle][PackLayers.Base];
            SheetSet shadowSheets = art.Animations[AnimationSpec.Idle][PackLayers.Shadow];
            Assert.That((baseSheets.FrameWidth, baseSheets.FrameHeight), Is.EqualTo((color.Width, color.Height)), "base frame size");
            Assert.That((shadowSheets.FrameWidth, shadowSheets.FrameHeight), Is.EqualTo((shadow.Width, shadow.Height)), "shadow frame size");
            Image8 frame = baseSheets.Files[0];
            Assert.That(new[] { frame[3, 3, 0], frame[3, 3, 1], frame[3, 3, 2], frame[3, 3, 3] },
                Is.EqualTo(new[] { (byte)grey, (byte)grey, (byte)grey, (byte)alpha }));
            Assert.That(shadowSheets.Files[0].Alpha(3, 3), Is.EqualTo(128), "every 2x2 block is half covered");
        }

        [Test]
        public void FlippedShadowsMirrorTheCharacterAndItsOrientation()
        {
            var renderer = new RecordingRenderer();
            new AvatarExporter(new DragonPoseSource(BodyShape.Female), renderer, Small()).Render();
            List<FrameRequest> runGun = renderer.Requests.Where(r => r.Animation == AnimationSpec.RunGun).ToList();
            List<FrameRequest> mirrored = runGun.Where(r => r.Mirrored).ToList();
            Assert.That(mirrored.Count, Is.EqualTo(18 * 12));
            Assert.That(mirrored.All(r => r.Pass == RenderPass.Shadow));
            foreach (FrameRequest r in mirrored)
            {
                double original = AnimationSpec.Direction(AnimationSpec.RunGun, r.Direction).Orientation;
                Assert.That(r.Orientation, Is.EqualTo(MathUtil.PositiveModulo(1 - original, 1)).Within(1e-12));
            }
            Assert.That(renderer.Requests.Where(r => r.Animation != AnimationSpec.RunGun).All(r => !r.Mirrored));
        }

        [Test]
        public void EachLayerKeepsOneCanvasForAllItsFrames()
        {
            var renderer = new RecordingRenderer();
            new AvatarExporter(new DragonPoseSource(BodyShape.Male), renderer, Small()).Render();
            foreach (IGrouping<(string, RenderPass, bool), FrameRequest> layer in
                     renderer.Requests.GroupBy(r => (r.Animation, r.Pass, r.Mirrored)))
            {
                Assert.That(layer.Select(r => (r.Canvas.Width, r.Canvas.Height, r.Canvas.AnchorX, r.Canvas.AnchorY)).Distinct().Count(),
                    Is.EqualTo(1), layer.Key.ToString());
            }
        }

        [Test]
        public void WrongImageSizesAreReported()
        {
            var renderer = new RecordingRenderer { WrongSizeAfter = 5 };
            var exporter = new AvatarExporter(new DragonPoseSource(BodyShape.Male), renderer, Small());
            var e = Assert.Throws<InvalidOperationException>(() => exporter.Render());
            Assert.That(e.Message, Does.Contain("idle direction"));
            Assert.That(e.Message, Does.Contain("expected"));
        }

        [Test]
        public void DrawingOutsideTheMeasuredBoundsIsReported()
        {
            var exporter = new AvatarExporter(GenericRig(), new ShrunkBoundsRenderer(), Small());
            var e = Assert.Throws<InvalidOperationException>(() => exporter.Render());
            Assert.That(e.Message, Does.Contain("outside the bounds MeasureWorldBounds reported"));
            Assert.That(e.Message, Does.Contain("idle direction 1 frame 1 color pass"));
            Assert.That(e.Message, Does.Contain("CanvasMarginPixels"));

            exporter = new AvatarExporter(GenericRig(), new RecordingRenderer { ShadowInCorner = true }, Small());
            e = Assert.Throws<InvalidOperationException>(() => exporter.Render());
            Assert.That(e.Message, Does.Contain("idle direction 1 frame 1 shadow pass has a drawn pixel at 0,0"));
        }

        [Test]
        public void AllowingCutOffExportsAndListsTheFramesThatReachTheEdge()
        {
            ExportSettings settings = Small();
            settings.AllowCutOff = true;
            var exporter = new AvatarExporter(GenericRig(), new ShrunkBoundsRenderer(), settings);
            ExportedArt art = exporter.Render();
            Assert.That(art.Animations, Does.ContainKey(AnimationSpec.Idle));
            Assert.That(exporter.CutOff, Is.Not.Empty);
            Assert.That(exporter.CutOff[0], Does.Contain("idle direction 1 frame 1 color pass"));

            var strict = new AvatarExporter(GenericRig(), new StickFigureRenderer(), Small());
            strict.Render();
            Assert.That(strict.CutOff, Is.Empty);
        }

        [Test]
        public void WithoutAMarginFramesMayReachTheCanvasEdge()
        {
            // some of the stick figure's frames touch the edge of canvases without a margin
            ExportSettings settings = Small();
            settings.CanvasMarginPixels = 0;
            ExportedArt art = new AvatarExporter(GenericRig(), new StickFigureRenderer(), settings).Render();
            Assert.That(art.Animations[AnimationSpec.Idle][PackLayers.Base].FrameWidth, Is.GreaterThan(4));
        }

        [TestCase(64, 1024, "run-gun shadow", 18)]
        [TestCase(32, 512, "run-gun shadow", 18)]
        public void SheetsThatDoNotFitNameTheAnimationAndLayer(int pixelsPerTile, int maxSheetSize, string layer, int directions)
        {
            // The stick figure's run-gun shadow is about 100 px wide and too wide and tall for 1024 px sheets
            // at 64 pixels per tile, and for 512 px sheets at 32.
            var settings = new ExportSettings
            {
                PixelsPerTile = pixelsPerTile,
                Supersampling = 1,
                Flight = false,
                MaxSheetSize = maxSheetSize,
            };
            var exporter = new AvatarExporter(GenericRig(), new StickFigureRenderer(), settings);
            var e = Assert.Throws<InvalidOperationException>(() => exporter.Render());
            Assert.That(e.Message, Does.StartWith($"the {layer} frames do not fit"));
            Assert.That(e.Message, Does.Contain($"at most {maxSheetSize / 12} px wide or {maxSheetSize / directions} px high"));
            Assert.That(e.Message.Contains("32 pixels per tile"), Is.EqualTo(pixelsPerTile == 64), "halving");
            Assert.That(e.Message.Contains("raise MaxSheetSize"), Is.EqualTo(maxSheetSize < 4096), "larger sheets");
            Assert.That(e.Message, Does.Not.Contain("fewer frames"));
            Assert.That(e.InnerException, Is.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void RowsTooWideAreSplitByFrames()
        {
            // the grey renderer fills canvases 12 tiles wide: 12 run frames of 384 px are 4608 px, but 8 directions fit
            var settings = new ExportSettings
            {
                PixelsPerTile = 32,
                Supersampling = 1,
                Flight = false,
                CanvasMarginPixels = 0,
            };
            ExportedArt art = new AvatarExporter(GenericRig(), new GreyRenderer { HalfWidth = 6 }, settings).Render();
            SheetSet run = art.Animations[AnimationSpec.Run][PackLayers.Base];
            Assert.That(run.FramesPerFile, Is.EqualTo(4096 / run.FrameWidth));
            Assert.That(run.LinesPerFile, Is.Null);
            Assert.That(run.Files.Count, Is.EqualTo((12 + run.FramesPerFile.Value - 1) / run.FramesPerFile.Value));
        }

        [Test]
        public void CanBeCancelled()
        {
            using (var cancel = new CancellationTokenSource())
            {
                var renderer = new RecordingRenderer();
                var exporter = new AvatarExporter(new DragonPoseSource(BodyShape.Male), renderer, Small());
                var progress = new ListProgress(new List<ExportProgress>(), p =>
                {
                    if (p.Done == 10)
                    {
                        cancel.Cancel();
                    }
                });
                Assert.Throws<OperationCanceledException>(() => exporter.Render(progress, cancel.Token));
                Assert.That(renderer.Requests.Count, Is.LessThan(13));
            }
        }

        [Test]
        public void RejectsBadSettings()
        {
            var renderer = new RecordingRenderer();
            var poses = new DragonPoseSource(BodyShape.Male);
            Assert.Throws<ArgumentException>(() => new AvatarExporter(poses, renderer, new ExportSettings { PixelsPerTile = 48 }));
            Assert.Throws<ArgumentException>(() => new AvatarExporter(poses, renderer, new ExportSettings { Supersampling = 0 }));
            Assert.Throws<ArgumentException>(() => new AvatarExporter(poses, renderer, new ExportSettings { MaxSheetSize = 5000 }));
            Assert.Throws<ArgumentNullException>(() => new AvatarExporter(null, renderer, new ExportSettings()));
        }

        [Test]
        public void HumanoidPackFromTheStickRendererIsValid()
        {
            var rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.6));
            var poses = new HumanoidAnimations(rig);
            var exporter = new AvatarExporter(poses, new StickFigureRenderer(), Small(true));
            ExportedArt art = exporter.Render();
            var content = new PackContent
            {
                Id = "exporter-test",
                Title = "Exporter test",
                SourceAvatar = "synthetic rig",
                Permission = "test data",
                GeneratorVersion = "0.0.1",
            };
            exporter.Fill(content, art);
            Assert.That(content.Timing.DistancePerFrame, Is.EqualTo(poses.Timing.DistancePerFrame));
            Assert.That(content.PixelsPerTile, Is.EqualTo(32));
            Assert.That(content.Flight, Is.True);
            Assert.That(PackValidator.Validate(content), Is.Empty);
            // every layer has something drawn: no 1x1 placeholder frames
            foreach (KeyValuePair<string, Dictionary<string, SheetSet>> animation in content.Animations)
            {
                foreach (KeyValuePair<string, SheetSet> layer in animation.Value)
                {
                    Assert.That(layer.Value.FrameWidth, Is.GreaterThan(4), animation.Key + " " + layer.Key);
                }
            }

            PythonTools.RequirePython();
            string dir = Path.Combine(Path.GetTempPath(), "exporter-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var output = new DirectoryPackOutput(dir, false);
                new PackWriter(PngWriter.Encode).Write(content, output);
                string printed = PythonTools.Run(
                    "import subprocess, sys\n" +
                    "r = subprocess.run([sys.executable, 'tools/validate_sprites.py', '--pack', sys.argv[1]], capture_output=True, text=True)\n" +
                    "print(r.returncode, r.stdout, r.stderr)",
                    "", output.PackDirectory);
                Assert.That(printed, Does.StartWith("0 OK:"), printed);
                Assert.That(printed, Does.Not.Contain("warning"), printed);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private sealed class ListProgress : IProgress<ExportProgress>
        {
            private readonly List<ExportProgress> list;
            private readonly Action<ExportProgress> also;

            public ListProgress(List<ExportProgress> list, Action<ExportProgress> also = null)
            {
                this.list = list;
                this.also = also;
            }

            public void Report(ExportProgress value)
            {
                list.Add(value);
                also?.Invoke(value);
            }
        }
    }

    public class PackArtTests
    {
        private static Image8 Figure()
        {
            var image = new Image8(60, 100);
            for (int y = 10; y < 90; y++)
            {
                for (int x = 20; x < 40; x++)
                {
                    image[x, y, 0] = (byte)(y < 30 ? 250 : 40);
                    image[x, y, 3] = 255;
                }
            }
            return image;
        }

        [Test]
        public void IconShowsTheTopOfTheFigure()
        {
            Image8 icon = PackArt.Icon(Figure());
            Assert.That((icon.Width, icon.Height), Is.EqualTo((64, 64)));
            // the bright head (top 20 of 80 rows) is in the upper part of the icon
            Assert.That(icon[32, 5, 0], Is.GreaterThan(200));
            Assert.That(icon[32, 5, 3], Is.EqualTo(255));
        }

        private static readonly (byte, byte, byte) Skin = (240, 200, 160);
        private static readonly (byte, byte, byte) Shoulders = (60, 200, 60);
        private static readonly (byte, byte, byte) ShoulderTips = (230, 230, 40);
        private static readonly (byte, byte, byte) Torso = (128, 128, 128);
        private static readonly (byte, byte, byte) Legs = (40, 40, 220);

        private static void Paint(Image8 image, int x0, int y0, int x1, int y1, (byte r, byte g, byte b) color)
        {
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    image[x, y, 0] = color.r;
                    image[x, y, 1] = color.g;
                    image[x, y, 2] = color.b;
                    image[x, y, 3] = 255;
                }
            }
        }

        /// <summary>The columns of the opaque pixels that have exactly this color.</summary>
        private static List<int> ColumnsOf(Image8 image, (byte r, byte g, byte b) color)
        {
            var columns = new List<int>();
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (image.Alpha(x, y) == 255 && (image[x, y, 0], image[x, y, 1], image[x, y, 2]) == color)
                    {
                        columns.Add(x);
                    }
                }
            }
            return columns;
        }

        [Test]
        public void IconOfARealisticFigureEndsBelowTheShoulders()
        {
            // 170 px tall, shoulders 52 px wide down to row 48, legs from row 100
            var figure = new Image8(60, 200);
            Paint(figure, 22, 10, 38, 34, Skin);
            Paint(figure, 4, 34, 56, 48, Shoulders);
            Paint(figure, 4, 34, 8, 48, ShoulderTips);
            Paint(figure, 52, 34, 56, 48, ShoulderTips);
            Paint(figure, 18, 48, 42, 100, Torso);
            Paint(figure, 20, 100, 40, 180, Legs);
            Image8 icon = PackArt.Icon(figure, 50);
            Assert.That(ColumnsOf(icon, Skin), Is.Not.Empty, "head");
            Assert.That(ColumnsOf(icon, Shoulders), Is.Not.Empty, "shoulders");
            List<int> tips = ColumnsOf(icon, ShoulderTips);
            Assert.That(tips.Count > 0 && tips.Min() < 8 && tips.Max() >= 56, "both shoulder tips, at the sides");
            Assert.That(ColumnsOf(icon, Legs), Is.Empty, "no legs");
        }

        [Test]
        public void IconOfABigHeadedFigureShowsItsShoulders()
        {
            // the head is half of the 120 px figure, and wider than the shoulders
            var figure = new Image8(60, 140);
            Paint(figure, 5, 10, 55, 70, Skin);
            Paint(figure, 15, 70, 45, 80, Shoulders);
            Paint(figure, 20, 80, 40, 100, Torso);
            Paint(figure, 22, 100, 38, 130, Legs);
            Image8 icon = PackArt.Icon(figure, 82);
            Assert.That(ColumnsOf(icon, Skin), Is.Not.Empty, "head");
            Assert.That(ColumnsOf(icon, Shoulders), Is.Not.Empty, "shoulders");
            Assert.That(ColumnsOf(icon, Legs), Is.Empty, "no legs");
            Assert.That(ColumnsOf(PackArt.Icon(figure, 130), Legs), Is.Empty, "at most 60% of the figure");
        }

        [Test]
        public void ThumbnailFitsTheWholeFigureOnADarkSquare()
        {
            Image8 thumbnail = PackArt.Thumbnail(Figure());
            Assert.That((thumbnail.Width, thumbnail.Height), Is.EqualTo((144, 144)));
            Assert.That(thumbnail[0, 0, 3], Is.EqualTo(255), "opaque background");
            Assert.That(thumbnail[72, 72, 0], Is.EqualTo(40), "the figure's body in the middle");
            Assert.That(thumbnail[72, 12, 0], Is.GreaterThan(200), "its head near the top");
        }

        [Test]
        public void EmptyFramesAreRejected()
        {
            Assert.Throws<InvalidOperationException>(() => PackArt.Icon(new Image8(8, 8)));
            Assert.Throws<ArgumentNullException>(() => PackArt.Thumbnail(null));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Pack
{
    public class SheetBuilderTests
    {
        private static List<IReadOnlyList<Image8>> Blank(int directions, int frames, int width, int height)
        {
            var result = new List<IReadOnlyList<Image8>>();
            for (int d = 0; d < directions; d++)
            {
                result.Add(Enumerable.Range(0, frames).Select(_ => new Image8(width, height)).ToList());
            }
            return result;
        }

        private static void Dot(Image8 image, int x, int y, byte alpha = 255) =>
            SyntheticPack.Paint(image, x, y, 10, 20, 30, alpha);

        [Test]
        public void OnePixelGetsAOnePixelMargin()
        {
            List<IReadOnlyList<Image8>> frames = Blank(1, 1, 10, 10);
            Dot(frames[0][0], 4, 5);
            SheetSet s = SheetBuilder.Build(frames, 5, 8, 64);
            Assert.That((s.FrameWidth, s.FrameHeight), Is.EqualTo((3, 3)));
            Assert.That(s.Files.Count, Is.EqualTo(1));
            Assert.That(s.LinesPerFile, Is.Null);
            Assert.That((s.DirectionCount, s.FrameCount), Is.EqualTo((1, 1)));
            // crop (3, 4)-(6, 7): centre (4.5, 5.5), anchor (5, 8) -> (-0.5, -2.5) px / 64, rounded to 5 places
            Assert.That(s.ShiftX, Is.EqualTo(-0.00781));
            Assert.That(s.ShiftY, Is.EqualTo(-0.03906));
            Image8 sheet = s.Files[0];
            Assert.That(sheet.Alpha(1, 1), Is.EqualTo(255));
            Assert.That(sheet[1, 1, 2], Is.EqualTo(30));
            Assert.That(sheet.Pixels.Count(b => b != 0), Is.EqualTo(4), "only the dot is drawn");
        }

        [Test]
        public void CropIsTheUnionOverFramesAndDirections()
        {
            List<IReadOnlyList<Image8>> frames = Blank(2, 2, 20, 20);
            Dot(frames[0][1], 3, 4);
            Dot(frames[1][0], 9, 12);
            SheetSet s = SheetBuilder.Build(frames, 10, 10, 64);
            // union (3, 4)-(10, 13), grown to (2, 3)-(11, 14)
            Assert.That((s.FrameWidth, s.FrameHeight), Is.EqualTo((9, 11)));
            Image8 sheet = s.Files[0];
            Assert.That((sheet.Width, sheet.Height), Is.EqualTo((18, 22)));
            // direction 0 frame 1 is the second column of the first row; direction 1 frame 0 the first column of the second
            Assert.That(sheet.Alpha(9 + 1, 1), Is.EqualTo(255));
            Assert.That(sheet.Alpha(7, 11 + 9), Is.EqualTo(255));
            Assert.That(sheet.Pixels.Count(b => b == 255), Is.EqualTo(2));
            Assert.That(s.ShiftX, Is.EqualTo(DecimalRounding.Round((2 + 4.5 - 10) / 64, 5)));
            Assert.That(s.ShiftY, Is.EqualTo(DecimalRounding.Round((3 + 5.5 - 10) / 64, 5)));
        }

        [Test]
        public void MarginIsClampedAtTheCanvasEdges()
        {
            List<IReadOnlyList<Image8>> frames = Blank(1, 2, 7, 5);
            Dot(frames[0][0], 0, 0);
            Dot(frames[0][1], 6, 4);
            SheetSet s = SheetBuilder.Build(frames, 3, 4, 64);
            Assert.That((s.FrameWidth, s.FrameHeight), Is.EqualTo((7, 5)));
            // centre (3.5, 2.5), anchor (3, 4): 0.5 / 64 = 0.0078125 -> 0.00781, -1.5 / 64 = -0.0234375 -> -0.02344
            Assert.That(s.ShiftX, Is.EqualTo(0.00781));
            Assert.That(s.ShiftY, Is.EqualTo(-0.02344));

            List<IReadOnlyList<Image8>> oneSide = Blank(1, 1, 7, 5);
            Dot(oneSide[0][0], 6, 2);
            SheetSet t = SheetBuilder.Build(oneSide, 3, 4, 64);
            Assert.That((t.FrameWidth, t.FrameHeight), Is.EqualTo((2, 3)), "grown left, clamped right");
        }

        [Test]
        public void TransparentPixelsDoNotWidenTheCropButAreCopied()
        {
            List<IReadOnlyList<Image8>> frames = Blank(1, 1, 12, 12);
            Dot(frames[0][0], 5, 5);
            SyntheticPack.Paint(frames[0][0], 0, 0, 255, 255, 255, 0);
            SyntheticPack.Paint(frames[0][0], 4, 4, 77, 0, 0, 0);
            SheetSet s = SheetBuilder.Build(frames, 6, 6, 64);
            Assert.That((s.FrameWidth, s.FrameHeight), Is.EqualTo((3, 3)));
            Assert.That(s.Files[0][0, 0, 0], Is.EqualTo(77), "colour of a transparent pixel inside the crop is kept");
        }

        [Test]
        public void EmptyLayerIsOnePixelAtTheAnchorPlusMargin()
        {
            SheetSet s = SheetBuilder.Build(Blank(8, 3, 10, 10), 4, 6, 64);
            Assert.That((s.FrameWidth, s.FrameHeight), Is.EqualTo((3, 3)));
            Assert.That(s.ShiftX, Is.EqualTo(0.00781)); // centre 4.5 - anchor 4 = 0.5 px
            Assert.That(s.ShiftY, Is.EqualTo(0.00781));
            Assert.That(s.Files[0].Pixels.All(b => b == 0));
            Assert.That((s.Files[0].Width, s.Files[0].Height), Is.EqualTo((9, 24)));

            // at the corner the margin is clamped; 1 px / 64 = 0.015625 is a tie, rounded to even like Python
            SheetSet corner = SheetBuilder.Build(Blank(1, 1, 10, 10), 0, 0, 64);
            Assert.That((corner.FrameWidth, corner.FrameHeight), Is.EqualTo((2, 2)));
            Assert.That(corner.ShiftX, Is.EqualTo(0.01562));
            Assert.That(corner.ShiftY, Is.EqualTo(0.01562));
        }

        [Test]
        public void EmptyLayerNeedsTheAnchorOnTheCanvas()
        {
            Assert.Throws<ArgumentException>(() => SheetBuilder.Build(Blank(1, 1, 10, 10), 10, 5, 64));
            Assert.Throws<ArgumentException>(() => SheetBuilder.Build(Blank(1, 1, 10, 10), 5, -1, 64));
            // with something drawn, the anchor may be anywhere
            List<IReadOnlyList<Image8>> frames = Blank(1, 1, 10, 10);
            Dot(frames[0][0], 5, 5);
            Assert.That(SheetBuilder.Build(frames, 5, 40, 64).ShiftY, Is.EqualTo(DecimalRounding.Round((4 + 1.5 - 40) / 64, 5)));
        }

        [Test]
        public void ShiftIsInTilesOfTheGivenPixelsPerTile()
        {
            List<IReadOnlyList<Image8>> frames = Blank(1, 1, 40, 40);
            Dot(frames[0][0], 30, 2);
            SheetSet s = SheetBuilder.Build(frames, 10, 20, 32);
            // crop (29, 1)-(32, 4): centre (30.5, 2.5) -> (20.5, -17.5) px / 32
            Assert.That(s.ShiftX, Is.EqualTo(0.64062)); // 0.640625 is a tie: to even
            Assert.That(s.ShiftY, Is.EqualTo(-0.54688)); // -0.546875 is a tie: to even
        }

        /// <summary>Frames whose crop is exactly 4 x 10 px, with each direction's row marked by its red value.</summary>
        private static List<IReadOnlyList<Image8>> TenPixelRows(int directions, int frames)
        {
            List<IReadOnlyList<Image8>> result = Blank(directions, frames, 6, 14);
            for (int d = 0; d < directions; d++)
            {
                for (int f = 0; f < frames; f++)
                {
                    SyntheticPack.Paint(result[d][f], 2, 3, (byte)(d + 1), (byte)(f + 1), 0, 255);
                    SyntheticPack.Paint(result[d][f], 3, 10, (byte)(d + 1), (byte)(f + 1), 0, 255);
                }
            }
            return result;
        }

        [TestCase(80, null, new[] { 80 })]
        [TestCase(4096, null, new[] { 80 })]
        [TestCase(79, 7, new[] { 70, 10 })]
        [TestCase(40, 4, new[] { 40, 40 })]
        [TestCase(39, 3, new[] { 30, 30, 20 })]
        [TestCase(10, 1, new[] { 10, 10, 10, 10, 10, 10, 10, 10 })]
        public void SplitsByRowsAtTheSheetLimit(int maxSheet, int? lines, int[] heights)
        {
            SheetSet s = SheetBuilder.Build(TenPixelRows(8, 2), 3, 12, 64, maxSheet);
            Assert.That((s.FrameWidth, s.FrameHeight), Is.EqualTo((4, 10)));
            Assert.That(s.LinesPerFile, Is.EqualTo(lines));
            Assert.That(s.Files.Select(f => f.Height), Is.EqualTo(heights));
            Assert.That(s.Files.Select(f => f.Width), Is.All.EqualTo(8));
            int perFile = lines ?? 8;
            for (int d = 0; d < 8; d++)
            {
                Image8 file = s.Files[d / perFile];
                int top = (d % perFile) * 10;
                for (int f = 0; f < 2; f++)
                {
                    // (2, 3) on the canvas is (1, 1) in the crop starting at (1, 2)
                    Assert.That(file[f * 4 + 1, top + 1, 0], Is.EqualTo(d + 1), $"direction {d} frame {f}");
                    Assert.That(file[f * 4 + 1, top + 1, 1], Is.EqualTo(f + 1));
                }
            }
        }

        [Test]
        public void RefusesSheetsLargerThanTheLimit()
        {
            // 8 frames of 4 px = 32 px wide, frames 10 px high
            Assert.That(SheetBuilder.Build(TenPixelRows(2, 8), 3, 12, 64, 32).Files[0].Width, Is.EqualTo(32));
            // too wide: split by frames, 7 per file
            SheetSet split = SheetBuilder.Build(TenPixelRows(2, 8), 3, 12, 64, 31);
            Assert.That(split.FramesPerFile, Is.EqualTo(7));
            Assert.That(split.Files.Select(f => (f.Width, f.Height)), Is.EqualTo(new[] { (28, 20), (4, 20) }));
            // too wide and too tall at once
            var both = Assert.Throws<InvalidOperationException>(() => SheetBuilder.Build(TenPixelRows(4, 8), 3, 12, 64, 31));
            StringAssert.Contains("4 directions of 8 frames of 4x10 px", both.Message);
            Assert.That(SheetBuilder.Build(TenPixelRows(1, 1), 3, 12, 64, 10).FrameHeight, Is.EqualTo(10));
            Assert.Throws<InvalidOperationException>(() => SheetBuilder.Build(TenPixelRows(1, 1), 3, 12, 64, 9));
        }

        [Test]
        public void ChecksItsInput()
        {
            Assert.Throws<ArgumentException>(() => SheetBuilder.Build(null, 0, 0, 64));
            Assert.Throws<ArgumentException>(() => SheetBuilder.Build(new List<IReadOnlyList<Image8>>(), 0, 0, 64));
            Assert.Throws<ArgumentException>(() => SheetBuilder.Build(new List<IReadOnlyList<Image8>> { new List<Image8>() }, 0, 0, 64));
            List<IReadOnlyList<Image8>> ragged = Blank(2, 2, 5, 5);
            ragged[1] = new List<Image8> { new Image8(5, 5) };
            Assert.Throws<ArgumentException>(() => SheetBuilder.Build(ragged, 0, 0, 64));
            List<IReadOnlyList<Image8>> mixed = Blank(2, 2, 5, 5);
            mixed[1] = new List<Image8> { new Image8(5, 5), new Image8(6, 5) };
            Assert.Throws<ArgumentException>(() => SheetBuilder.Build(mixed, 0, 0, 64));
            List<IReadOnlyList<Image8>> missing = Blank(1, 2, 5, 5);
            missing[0] = new List<Image8> { new Image8(5, 5), null };
            Assert.Throws<ArgumentException>(() => SheetBuilder.Build(missing, 0, 0, 64));
            Assert.Throws<ArgumentOutOfRangeException>(() => SheetBuilder.Build(Blank(1, 1, 5, 5), 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SheetBuilder.Build(Blank(1, 1, 5, 5), 0, 0, 64, 0));
        }

        [Test]
        public void RandomLayersKeepEveryDrawnPixel()
        {
            var rng = new Random(7);
            for (int iteration = 0; iteration < 60; iteration++)
            {
                int width = rng.Next(1, 30), height = rng.Next(1, 30);
                int directions = rng.Next(1, 10), frameCount = rng.Next(1, 6);
                List<IReadOnlyList<Image8>> frames = RandomFrames(rng, directions, frameCount, width, height);
                int anchorX = rng.Next(width), anchorY = rng.Next(height);
                int maxSheet = rng.Next(Math.Max(width, height), 200);
                (int x0, int y0, int x1, int y1) = ExpectedCrop(frames, anchorX, anchorY);
                bool wide = frameCount * (x1 - x0) > maxSheet, tall = directions * (y1 - y0) > maxSheet;
                if (wide && tall)
                {
                    Assert.Throws<InvalidOperationException>(() => SheetBuilder.Build(frames, anchorX, anchorY, 64, maxSheet));
                    continue;
                }
                SheetSet s = SheetBuilder.Build(frames, anchorX, anchorY, 64, maxSheet);

                int lines = s.LinesPerFile ?? directions, perFile = s.FramesPerFile ?? frameCount;
                Assert.That(s.Files.All(f => f.Height <= maxSheet && f.Width <= maxSheet));
                Assert.That(s.LinesPerFile.HasValue, Is.EqualTo(tall));
                Assert.That(s.FramesPerFile.HasValue, Is.EqualTo(wide));
                Assert.That((s.FrameWidth, s.FrameHeight), Is.EqualTo((x1 - x0, y1 - y0)));
                for (int d = 0; d < directions; d++)
                {
                    for (int f = 0; f < frameCount; f++)
                    {
                        Image8 frame = frames[d][f];
                        Image8 file = s.Files[wide ? f / perFile : d / lines];
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                bool inside = x >= x0 && x < x0 + s.FrameWidth && y >= y0 && y < y0 + s.FrameHeight;
                                if (!inside)
                                {
                                    Assert.That(frame.Alpha(x, y), Is.EqualTo(0), "a drawn pixel is outside the crop");
                                    continue;
                                }
                                int sx = (f % perFile) * s.FrameWidth + x - x0, sy = (d % lines) * s.FrameHeight + y - y0;
                                for (int c = 0; c < 4; c++)
                                {
                                    Assert.That(file[sx, sy, c], Is.EqualTo(frame[x, y, c]));
                                }
                            }
                        }
                    }
                }
                Assert.That(s.ShiftX, Is.EqualTo(DecimalRounding.Round((x0 + s.FrameWidth / 2.0 - anchorX) / 64, 5)));
                Assert.That(s.ShiftY, Is.EqualTo(DecimalRounding.Round((y0 + s.FrameHeight / 2.0 - anchorY) / 64, 5)));
            }
        }

        private static List<IReadOnlyList<Image8>> RandomFrames(Random rng, int directions, int frameCount, int width, int height)
        {
            List<IReadOnlyList<Image8>> frames = Blank(directions, frameCount, width, height);
            bool empty = rng.Next(5) == 0;
            foreach (Image8 frame in frames.SelectMany(row => row))
            {
                int dots = empty ? 0 : rng.Next(0, 4);
                for (int i = 0; i < dots; i++)
                {
                    SyntheticPack.Paint(frame, rng.Next(width), rng.Next(height), (byte)rng.Next(256), (byte)rng.Next(256),
                        (byte)rng.Next(256), (byte)rng.Next(1, 256));
                }
                // colour without alpha does not count
                SyntheticPack.Paint(frame, rng.Next(width), rng.Next(height), 99, 99, 99, 0);
            }
            return frames;
        }

        /// <summary>The crop, recomputed pixel by pixel: the opaque union (or the anchor pixel) grown by one, clamped.</summary>
        private static (int x0, int y0, int x1, int y1) ExpectedCrop(List<IReadOnlyList<Image8>> frames, int anchorX, int anchorY)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            foreach (Image8 frame in frames.SelectMany(row => row))
            {
                for (int y = 0; y < frame.Height; y++)
                {
                    for (int x = 0; x < frame.Width; x++)
                    {
                        if (frame.Alpha(x, y) > 0)
                        {
                            minX = Math.Min(minX, x);
                            minY = Math.Min(minY, y);
                            maxX = Math.Max(maxX, x);
                            maxY = Math.Max(maxY, y);
                        }
                    }
                }
            }
            if (maxX < 0)
            {
                (minX, minY, maxX, maxY) = (anchorX, anchorY, anchorX, anchorY);
            }
            Image8 first = frames[0][0];
            return (Math.Max(minX - 1, 0), Math.Max(minY - 1, 0), Math.Min(maxX + 2, first.Width), Math.Min(maxY + 2, first.Height));
        }

    }
}

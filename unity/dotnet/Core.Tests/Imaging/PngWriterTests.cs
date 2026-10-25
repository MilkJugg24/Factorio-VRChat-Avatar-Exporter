using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Imaging
{
    public class PngWriterTests
    {
        private const int Stride = 16 * 4;

        /// <summary>The filter type of every row of a PNG written by PngWriter.</summary>
        private static int[] RowFilters(byte[] png, int width, int height)
        {
            byte[] scanlines = TestPng.ZlibDecompress(TestPng.ParseChunks(png).Single(c => c.Type == "IDAT").Data);
            int stride = width * 4;
            Assert.That(scanlines.Length, Is.EqualTo((stride + 1) * height));
            return Enumerable.Range(0, height).Select(y => (int)scanlines[y * (stride + 1)]).ToArray();
        }

        /// <summary>
        /// Rows built so that each filter type fits one row best: zeros (none), a ramp (sub), noise, the
        /// same noise again (up), the average of left and up (average), then blocks of flat color under
        /// blocks that keep some colors and change others, which only Paeth predicts from both the left
        /// and the row above.
        /// </summary>
        private static Image8 EveryFilterImage()
        {
            var rng = new Random(71);
            var rows = new byte[7][];
            rows[0] = new byte[Stride];
            rows[1] = Enumerable.Range(0, Stride).Select(i => (byte)(60 + 20 * (i / 4))).ToArray();
            rows[2] = new byte[Stride];
            rng.NextBytes(rows[2]);
            rows[3] = (byte[])rows[2].Clone();
            rows[4] = new byte[Stride];
            for (int i = 0; i < Stride; i++)
            {
                rows[4][i] = (byte)(((i >= 4 ? rows[4][i - 4] : 0) + rows[3][i]) / 2);
            }
            byte[] above = { 10, 80, 30, 200 };
            byte[] below = { 10, 90, 30, 210 };
            rows[5] = Enumerable.Range(0, Stride).Select(i => above[i / 16]).ToArray();
            rows[6] = Enumerable.Range(0, Stride).Select(i => below[i / 16]).ToArray();
            return new Image8(16, 7, rows.SelectMany(r => r).ToArray());
        }

        [Test]
        public void WritesSignatureIhdrOneIdatAndIend()
        {
            byte[] png = PngWriter.Encode(TestImages.Random8(new Random(72), 7, 3));
            List<(string Type, byte[] Data)> chunks = TestPng.ParseChunks(png); // checks every CRC
            Assert.That(chunks.Select(c => c.Type), Is.EqualTo(new[] { "IHDR", "IDAT", "IEND" }));
            byte[] ihdr = chunks[0].Data;
            Assert.That(TestPng.ReadUInt32(ihdr, 0), Is.EqualTo(7u));
            Assert.That(TestPng.ReadUInt32(ihdr, 4), Is.EqualTo(3u));
            Assert.That(ihdr.Skip(8), Is.EqualTo(new byte[] { 8, 6, 0, 0, 0 }), "8-bit RGBA, deflate, adaptive filters, no interlace");
            Assert.That(chunks[2].Data, Is.Empty);
        }

        [Test]
        public void ChoosesEveryFilterWhereItFitsBest()
        {
            Image8 image = EveryFilterImage();
            int[] filters = RowFilters(PngWriter.Encode(image), 16, 7);
            // rows 2 and 5 only prepare the rows below them
            Assert.That(new[] { filters[0], filters[1], filters[3], filters[4], filters[6] }, Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            TestImages.AssertEqual(PngReader.Decode(PngWriter.Encode(image)), image);
        }

        [Test]
        public void ChoosesTheFilterWithTheSmallestSignedSum()
        {
            var rng = new Random(73);
            for (int trial = 0; trial < 12; trial++)
            {
                int width = rng.Next(1, 30), height = rng.Next(1, 30);
                Image8 image = trial % 2 == 0 ? TestImages.Random8(rng, width, height) : TestImages.Smooth8(rng, width, height);
                int stride = width * 4;
                int[] chosen = RowFilters(PngWriter.Encode(image), width, height);
                for (int y = 0; y < height; y++)
                {
                    long[] scores = new long[5];
                    for (int f = 0; f < 5; f++)
                    {
                        for (int i = 0; i < stride; i++)
                        {
                            int v = (byte)(image.Pixels[y * stride + i] - TestPng.Predict(f, image.Pixels, stride, 4, y, i));
                            scores[f] += v < 128 ? v : 256 - v;
                        }
                    }
                    int best = Array.IndexOf(scores, scores.Min()); // the first, so ties go to the lower type
                    Assert.That(chosen[y], Is.EqualTo(best), $"trial {trial}, row {y}, scores {string.Join(", ", scores)}");
                }
            }
        }

        [Test]
        public void IsDeterministic()
        {
            Image8 image = TestImages.Smooth8(new Random(74), 50, 40);
            byte[] first = PngWriter.Encode(image);
            Assert.That(PngWriter.Encode(TestImages.Copy(image)), Is.EqualTo(first));
            Assert.That(PngWriter.Encode(image), Is.EqualTo(first));
        }

        [Test]
        public void PillowReadsTheSamePixels()
        {
            var rng = new Random(75);
            var images = new List<Image8>
            {
                TestImages.Random8(rng, 1, 1),
                TestImages.Random8(rng, 3, 5),
                TestImages.Random8(rng, 17, 9),
                TestImages.Smooth8(rng, 64, 33),
                EveryFilterImage(),
                new Image8(5, 4), // fully transparent
            };
            using var dir = new TempDirectory();
            for (int i = 0; i < images.Count; i++)
            {
                File.WriteAllBytes(dir.File($"{i}.png"), PngWriter.Encode(images[i]));
            }
            PythonTools.Run(
                "import sys, os\n" +
                "from PIL import Image\n" +
                "d, n = sys.argv[1], int(sys.argv[2])\n" +
                "for i in range(n):\n" +
                "    with Image.open(os.path.join(d, f'{i}.png')) as im:\n" +
                "        im.load()\n" +
                "        assert im.mode == 'RGBA', im.mode\n" +
                "        open(os.path.join(d, f'{i}.raw'), 'wb').write(im.tobytes())\n",
                dir.Path, images.Count.ToString());
            for (int i = 0; i < images.Count; i++)
            {
                Assert.That(File.ReadAllBytes(dir.File($"{i}.raw")), Is.EqualTo(images[i].Pixels), $"image {i}");
            }
        }

        [Test]
        public void RejectsNull()
        {
            Assert.That(() => PngWriter.Encode(null), Throws.ArgumentNullException);
        }
    }
}

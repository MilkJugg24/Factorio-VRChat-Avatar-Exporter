using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Imaging
{
    public class PngReaderTests
    {
        private static readonly int[] ColorTypes = { 0, 2, 4, 6 };

        /// <summary>What decoding samples of a color type must give.</summary>
        private static byte[] ExpectedRgba(int colorType, byte[] samples, int pixels)
        {
            int channels = TestPng.Channels(colorType);
            var rgba = new byte[pixels * 4];
            for (int p = 0; p < pixels; p++)
            {
                byte[] s = samples.Skip(p * channels).Take(channels).ToArray();
                byte[] px = colorType switch
                {
                    0 => new[] { s[0], s[0], s[0], (byte)255 },
                    2 => new[] { s[0], s[1], s[2], (byte)255 },
                    4 => new[] { s[0], s[0], s[0], s[1] },
                    _ => s,
                };
                px.CopyTo(rgba, p * 4);
            }
            return rgba;
        }

        private static byte[] RandomSamples(Random rng, int colorType, int width, int height)
        {
            var samples = new byte[width * height * TestPng.Channels(colorType)];
            rng.NextBytes(samples);
            return samples;
        }

        private static byte[] SmallPngWith(params byte[][] chunksAfterIhdr)
        {
            var chunks = new List<byte[]> { TestPng.Ihdr(2, 2, 8, 6) };
            chunks.AddRange(chunksAfterIhdr);
            return TestPng.File(chunks.ToArray());
        }

        private static byte[] SmallIdat(int rows = 2, int stride = 8) =>
            TestPng.Chunk("IDAT", TestPng.ZlibCompress(new byte[(stride + 1) * rows]));

        private static void AssertInvalid(byte[] png, string messagePart)
        {
            Assert.That(() => PngReader.Decode(png), Throws.InstanceOf<InvalidDataException>().With.Message.Contains(messagePart));
        }

        private static void AssertNotSupported(byte[] png, string messagePart)
        {
            Assert.That(() => PngReader.Decode(png), Throws.InstanceOf<NotSupportedException>().With.Message.Contains(messagePart));
        }

        [Test]
        public void RoundTripsThroughTheWriter()
        {
            var rng = new Random(81);
            var sizes = new List<(int, int)> { (1, 1), (1, 7), (7, 1), (2, 2), (3, 5), (13, 11), (31, 17), (64, 64) };
            for (int i = 0; i < 20; i++)
            {
                sizes.Add((rng.Next(1, 50), rng.Next(1, 50)));
            }
            foreach ((int width, int height) in sizes)
            {
                foreach (Image8 image in new[] { TestImages.Random8(rng, width, height), TestImages.Smooth8(rng, width, height) })
                {
                    TestImages.AssertEqual(PngReader.Decode(PngWriter.Encode(image)), image, $"{width}x{height}");
                }
            }
        }

        [Test]
        public void DecodesEveryColorTypeWithEveryFilter()
        {
            var rng = new Random(82);
            const int width = 9, height = 7;
            var filterChoices = new List<Func<int, int>> { _ => 0, _ => 1, _ => 2, _ => 3, _ => 4, y => y % 5, y => 4 - y % 5 };
            foreach (int colorType in ColorTypes)
            {
                for (int f = 0; f < filterChoices.Count; f++)
                {
                    byte[] samples = RandomSamples(rng, colorType, width, height);
                    byte[] png = TestPng.Build(width, height, colorType, samples, filterChoices[f]);
                    Image8 image = PngReader.Decode(png);
                    Assert.That(image.Width, Is.EqualTo(width));
                    Assert.That(image.Height, Is.EqualTo(height));
                    Assert.That(image.Pixels, Is.EqualTo(ExpectedRgba(colorType, samples, width * height)),
                        $"color type {colorType}, filters {f}");
                }
            }
        }

        [Test]
        public void JoinsImageDataSplitOverManyIdatChunks()
        {
            var rng = new Random(83);
            foreach (int parts in new[] { 2, 3, 7, 200 }) // 200 parts include empty IDAT chunks
            {
                byte[] samples = RandomSamples(rng, 6, 10, 6);
                byte[] png = TestPng.Build(10, 6, 6, samples, y => y % 5, parts);
                Assert.That(TestPng.ParseChunks(png).Count(c => c.Type == "IDAT"), Is.EqualTo(parts));
                Assert.That(PngReader.Decode(png).Pixels, Is.EqualTo(samples), $"{parts} parts");
            }
        }

        [Test]
        public void IgnoresAncillaryChunksAndASuggestedPalette()
        {
            var rng = new Random(84);
            byte[] samples = RandomSamples(rng, 2, 4, 3);
            byte[] zlib = TestPng.ZlibCompress(TestPng.Scanlines(samples, 12, 3, 3, _ => 0));
            byte[] png = TestPng.File(
                TestPng.Ihdr(4, 3, 8, 2),
                TestPng.Chunk("gAMA", new byte[] { 0, 0, 0xB1, 0x8F }),
                TestPng.Chunk("sRGB", new byte[] { 0 }),
                TestPng.Chunk("PLTE", new byte[] { 1, 2, 3, 4, 5, 6 }),
                TestPng.Chunk("tEXt", Encoding.ASCII.GetBytes("Comment\0hello")),
                TestPng.Chunk("IDAT", zlib),
                TestPng.Chunk("tIME", new byte[7]),
                TestPng.Iend());
            Assert.That(PngReader.Decode(png).Pixels, Is.EqualTo(ExpectedRgba(2, samples, 12)));
        }

        [Test]
        public void TrnsColorKeyMakesMatchingPixelsTransparent()
        {
            // gray: samples equal to 7 become transparent
            byte[] gray = { 7, 8, 7, 0 };
            byte[] grayPng = TestPng.File(TestPng.Ihdr(2, 2, 8, 0), TestPng.Chunk("tRNS", new byte[] { 0, 7 }),
                TestPng.Chunk("IDAT", TestPng.ZlibCompress(TestPng.Scanlines(gray, 2, 2, 1, _ => 0))), TestPng.Iend());
            Assert.That(PngReader.Decode(grayPng).Pixels.Where((_, i) => i % 4 == 3), Is.EqualTo(new byte[] { 0, 255, 0, 255 }));

            // RGB: only the exact color (1, 2, 3)
            byte[] rgb = { 1, 2, 3, 1, 2, 4, 3, 2, 1, 1, 2, 3 };
            byte[] Rgb(byte[] trns) => TestPng.File(TestPng.Ihdr(2, 2, 8, 2), TestPng.Chunk("tRNS", trns),
                TestPng.Chunk("IDAT", TestPng.ZlibCompress(TestPng.Scanlines(rgb, 6, 2, 3, _ => 1))), TestPng.Iend());
            Image8 keyed = PngReader.Decode(Rgb(new byte[] { 0, 1, 0, 2, 0, 3 }));
            Assert.That(keyed.Pixels.Where((_, i) => i % 4 == 3), Is.EqualTo(new byte[] { 0, 255, 255, 0 }));
            Assert.That(keyed[0, 0, 0], Is.EqualTo(1), "color kept under alpha 0");

            // a 16-bit key no 8-bit sample can match
            Assert.That(PngReader.Decode(Rgb(new byte[] { 1, 1, 0, 2, 0, 3 })).Pixels.Where((_, i) => i % 4 == 3), Is.All.EqualTo(255));
            AssertInvalid(Rgb(new byte[] { 0, 1, 0, 2 }), "tRNS");
        }

        [Test]
        public void IgnoresTrnsInImagesWithAlpha()
        {
            byte[] samples = RandomSamples(new Random(85), 6, 2, 2);
            byte[] png = TestPng.File(TestPng.Ihdr(2, 2, 8, 6), TestPng.Chunk("tRNS", new byte[] { 0, 1 }),
                TestPng.Chunk("IDAT", TestPng.ZlibCompress(TestPng.Scanlines(samples, 8, 2, 4, _ => 0))), TestPng.Iend());
            Assert.That(PngReader.Decode(png).Pixels, Is.EqualTo(samples));
        }

        [Test]
        public void DecodesPngsWrittenByPillow()
        {
            using var dir = new TempDirectory();
            string listing = PythonTools.Run(
                "import sys, os, random\n" +
                "from PIL import Image\n" +
                "d = sys.argv[1]\n" +
                "rng = random.Random(86)\n" +
                "cases = [('RGBA', 4, 37, 23), ('RGBA', 4, 1, 1), ('RGBA', 4, 300, 257), ('RGB', 3, 31, 17),\n" +
                "         ('RGB', 3, 1, 9), ('LA', 2, 13, 11), ('L', 1, 9, 21)]\n" +
                "for n, (mode, ch, w, h) in enumerate(cases):\n" +
                "    for optimize in (False, True):\n" +
                "        # half noise, half gradients, so encoders pick different filters\n" +
                "        data = bytes(rng.randrange(256) if (i // ch) % w < w // 2 else ((i // ch) % w * 7 + i // (ch * w) * 3 + i % ch * 50) % 256\n" +
                "                     for i in range(w * h * ch))\n" +
                "        im = Image.frombytes(mode, (w, h), data)\n" +
                "        name = f'{n}-{mode}-{int(optimize)}'\n" +
                "        im.save(os.path.join(d, name + '.png'), optimize=optimize)\n" +
                "        with Image.open(os.path.join(d, name + '.png')) as back:\n" +
                "            open(os.path.join(d, name + '.raw'), 'wb').write(back.convert('RGBA').tobytes())\n" +
                "        print(name, w, h)\n",
                dir.Path);
            string[] lines = listing.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.That(lines.Length, Is.EqualTo(14));
            foreach (string line in lines)
            {
                string[] parts = line.Split(' ');
                Image8 image = PngReader.Decode(File.ReadAllBytes(dir.File(parts[0] + ".png")));
                Assert.That(image.Width, Is.EqualTo(int.Parse(parts[1])), parts[0]);
                Assert.That(image.Height, Is.EqualTo(int.Parse(parts[2])), parts[0]);
                Assert.That(image.Pixels, Is.EqualTo(File.ReadAllBytes(dir.File(parts[0] + ".raw"))), parts[0]);
            }
        }

        [Test]
        public void RejectsACorruptedCrc()
        {
            byte[] png = PngWriter.Encode(TestImages.Random8(new Random(87), 8, 8));
            // a flipped bit in the image data, in the IHDR, and in a stored CRC
            int idatData = 8 + 25 + 8;
            foreach (int offset in new[] { idatData + 3, 8 + 8 + 2, png.Length - 1 })
            {
                var corrupt = (byte[])png.Clone();
                corrupt[offset] ^= 0x10;
                AssertInvalid(corrupt, "CRC mismatch");
            }
        }

        [Test]
        public void RejectsNonPngAndTruncatedFiles()
        {
            byte[] png = PngWriter.Encode(TestImages.Random8(new Random(88), 6, 5));
            AssertInvalid(Encoding.ASCII.GetBytes("GIF89a, definitely not a PNG"), "signature");
            AssertInvalid(png.Take(5).ToArray(), "signature");
            for (int length = 8; length < png.Length; length += 3)
            {
                Assert.That(() => PngReader.Decode(png.Take(length).ToArray()), Throws.InstanceOf<InvalidDataException>(), $"cut at {length}");
            }
            Assert.That(() => PngReader.Decode(null), Throws.ArgumentNullException);
        }

        [Test]
        public void RejectsBadChunkStructure()
        {
            byte[] ihdr = TestPng.Ihdr(2, 2, 8, 6);
            AssertInvalid(TestPng.File(SmallIdat(), ihdr, TestPng.Iend()), "first chunk is 'IDAT'");
            AssertInvalid(SmallPngWith(SmallIdat()), "no IEND");
            AssertInvalid(SmallPngWith(TestPng.Iend()), "no IDAT");
            AssertInvalid(SmallPngWith(ihdr, SmallIdat(), TestPng.Iend()), "more than one IHDR");
            AssertInvalid(SmallPngWith(SmallIdat(), TestPng.Chunk("tEXt", new byte[3]), SmallIdat(), TestPng.Iend()), "consecutive");
            AssertInvalid(SmallPngWith(TestPng.Chunk("I#AT", new byte[1]), SmallIdat(), TestPng.Iend()), "chunk type");
            AssertInvalid(TestPng.File(TestPng.Chunk("IHDR", new byte[12]), SmallIdat(), TestPng.Iend()), "IHDR is 12 bytes");
            AssertNotSupported(SmallPngWith(TestPng.Chunk("CRIT", new byte[1]), SmallIdat(), TestPng.Iend()), "'CRIT'");
        }

        [Test]
        public void RejectsInvalidHeaders()
        {
            byte[] File(byte[] ihdr) => TestPng.File(ihdr, SmallIdat(), TestPng.Iend());
            AssertInvalid(File(TestPng.Ihdr(0, 2, 8, 6)), "size");
            AssertInvalid(File(TestPng.Ihdr(2, -1, 8, 6)), "size");
            AssertInvalid(File(TestPng.Ihdr(2, 2, 8, 5)), "color type 5");
            AssertInvalid(File(TestPng.Ihdr(2, 2, 8, 6, 2)), "interlace method 2");
            byte[] badMethod = TestPng.Ihdr(2, 2, 8, 6);
            badMethod[8 + 10] = 1;
            AssertInvalid(File(TestPng.Chunk("IHDR", badMethod.Skip(8).Take(13).ToArray())), "compression or filter method");
        }

        [Test]
        public void RejectsUnsupportedFormats()
        {
            byte[] File(byte[] ihdr) => TestPng.File(ihdr, SmallIdat(), TestPng.Iend());
            AssertNotSupported(File(TestPng.Ihdr(2, 2, 8, 6, 1)), "interlaced");
            AssertNotSupported(File(TestPng.Ihdr(2, 2, 16, 6)), "16 bits");
            AssertNotSupported(File(TestPng.Ihdr(2, 2, 1, 0)), "1 bits");
            AssertNotSupported(File(TestPng.Ihdr(2, 2, 8, 3)), "palette");
            AssertNotSupported(File(TestPng.Ihdr(65536, 65536, 8, 6)), "too large");
        }

        [Test]
        public void RejectsBadImageData()
        {
            // unknown filter type 5 in the second row
            byte[] scanlines = new byte[18];
            scanlines[9] = 5;
            AssertInvalid(SmallPngWith(TestPng.Chunk("IDAT", TestPng.ZlibCompress(scanlines)), TestPng.Iend()), "filter type 5 in row 1");
            AssertInvalid(SmallPngWith(SmallIdat(rows: 1), TestPng.Iend()), "truncated");
            AssertInvalid(SmallPngWith(SmallIdat(rows: 3), TestPng.Iend()), "more than the expected");
            AssertInvalid(SmallPngWith(TestPng.Chunk("IDAT", new byte[] { 0x78, 0x9D, 1, 2, 3, 4, 5, 6 }), TestPng.Iend()), "check bits");
        }
    }
}

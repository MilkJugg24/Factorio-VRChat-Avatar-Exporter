using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Imaging
{
    public class ImageOpsTests
    {
        private static Image8 Solid(int width, int height, byte r, byte g, byte b, byte a)
        {
            var image = new Image8(width, height);
            for (int i = 0; i < image.Pixels.Length; i += 4)
            {
                image.Pixels[i] = r;
                image.Pixels[i + 1] = g;
                image.Pixels[i + 2] = b;
                image.Pixels[i + 3] = a;
            }
            return image;
        }

        private static byte[] Pixel(Image8 image, int x, int y) => Enumerable.Range(0, 4).Select(c => image[x, y, c]).ToArray();

        [Test]
        public void FlipRowsReversesRowOrder()
        {
            ImageF image = TestImages.RandomF(new Random(91), 5, 4);
            var original = (float[])image.Pixels.Clone();
            ImageF flipped = ImageOps.FlipRows(image);
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 5; x++)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        Assert.That(flipped[x, y, c], Is.EqualTo(image[x, 3 - y, c]));
                    }
                }
            }
            Assert.That(image.Pixels, Is.EqualTo(original), "input unchanged");
            Assert.That(ImageOps.FlipRows(flipped).Pixels, Is.EqualTo(original));
            Assert.That(ImageOps.FlipRows(new ImageF(3, 1)).Height, Is.EqualTo(1));
            Assert.That(() => ImageOps.FlipRows(null), Throws.ArgumentNullException);
        }

        [Test]
        public void CropCopiesTheRectangle()
        {
            Image8 image = TestImages.Random8(new Random(92), 9, 7);
            Image8 crop = ImageOps.Crop(image, 2, 3, 5, 4);
            Assert.That(crop.Width, Is.EqualTo(5));
            Assert.That(crop.Height, Is.EqualTo(4));
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 5; x++)
                {
                    Assert.That(Pixel(crop, x, y), Is.EqualTo(Pixel(image, x + 2, y + 3)));
                }
            }
            Image8 whole = ImageOps.Crop(image, 0, 0, 9, 7);
            Assert.That(whole.Pixels, Is.EqualTo(image.Pixels).And.Not.SameAs(image.Pixels));
            Assert.That(ImageOps.Crop(image, 8, 6, 1, 1).Pixels, Is.EqualTo(Pixel(image, 8, 6)));
        }

        [Test]
        public void CropRejectsRectanglesOutsideTheImage()
        {
            var image = new Image8(4, 3);
            foreach ((int x, int y, int w, int h) in new[] { (-1, 0, 2, 2), (0, -1, 2, 2), (3, 0, 2, 1), (0, 2, 1, 2), (0, 0, 0, 1), (0, 0, 1, 0), (1, 1, int.MaxValue, 1) })
            {
                Assert.That(() => ImageOps.Crop(image, x, y, w, h),
                    Throws.InstanceOf<ArgumentOutOfRangeException>().With.Message.Contains("4x3"), $"({x}, {y}) {w}x{h}");
            }
            Assert.That(() => ImageOps.Crop(null, 0, 0, 1, 1), Throws.ArgumentNullException);
        }

        /// <summary>Blit and CompositeOver against a per-pixel reference, at offsets inside, across and beyond every edge.</summary>
        private static void CheckClipping(Action<Image8, Image8, int, int> operation, Func<byte[], byte[], byte[]> pixelResult)
        {
            var rng = new Random(93);
            for (int trial = 0; trial < 60; trial++)
            {
                Image8 dst = TestImages.Random8(rng, rng.Next(1, 12), rng.Next(1, 12));
                Image8 src = TestImages.Random8(rng, rng.Next(1, 12), rng.Next(1, 12));
                int x = rng.Next(-14, 14), y = rng.Next(-14, 14);
                Image8 before = TestImages.Copy(dst);
                operation(dst, src, x, y);
                for (int dy = 0; dy < dst.Height; dy++)
                {
                    for (int dx = 0; dx < dst.Width; dx++)
                    {
                        int sx = dx - x, sy = dy - y;
                        bool covered = sx >= 0 && sy >= 0 && sx < src.Width && sy < src.Height;
                        byte[] expected = covered ? pixelResult(Pixel(before, dx, dy), Pixel(src, sx, sy)) : Pixel(before, dx, dy);
                        Assert.That(Pixel(dst, dx, dy), Is.EqualTo(expected), $"trial {trial}: pixel ({dx}, {dy}), offset ({x}, {y})");
                    }
                }
            }
        }

        [Test]
        public void BlitCopiesAndClips()
        {
            CheckClipping(ImageOps.Blit, (dst, src) => src);
        }

        [Test]
        public void BlitFarOutsideDoesNothing()
        {
            Image8 dst = TestImages.Random8(new Random(94), 4, 4);
            byte[] before = (byte[])dst.Pixels.Clone();
            var src = new Image8(3, 3);
            foreach ((int x, int y) in new[] { (4, 0), (0, 4), (-3, 0), (0, -3), (int.MaxValue, 0), (int.MinValue, int.MinValue) })
            {
                ImageOps.Blit(dst, src, x, y);
                ImageOps.CompositeOver(dst, Solid(3, 3, 1, 2, 3, 255), x, y);
            }
            Assert.That(dst.Pixels, Is.EqualTo(before));
        }

        [Test]
        public void BlitAndCompositeRejectBadArguments()
        {
            var image = new Image8(2, 2);
            Assert.That(() => ImageOps.Blit(image, image, 0, 0), Throws.ArgumentException.With.Message.Contains("different"));
            Assert.That(() => ImageOps.CompositeOver(image, image, 0, 0), Throws.ArgumentException);
            Assert.That(() => ImageOps.Blit(null, image, 0, 0), Throws.ArgumentNullException);
            Assert.That(() => ImageOps.Blit(image, null, 0, 0), Throws.ArgumentNullException);
            Assert.That(() => ImageOps.CompositeOver(null, image, 0, 0), Throws.ArgumentNullException);
            Assert.That(() => ImageOps.CompositeOver(image, null, 0, 0), Throws.ArgumentNullException);
        }

        [Test]
        public void CompositeOverKnownCases()
        {
            var dst = new Image8(4, 1);
            dst.Pixels[0] = 10; dst.Pixels[1] = 20; dst.Pixels[2] = 30; dst.Pixels[3] = 255; // opaque
            dst.Pixels[4] = 10; dst.Pixels[5] = 20; dst.Pixels[6] = 30; dst.Pixels[7] = 255;
            dst.Pixels[8] = 99; dst.Pixels[9] = 99; dst.Pixels[10] = 99; dst.Pixels[11] = 0;  // transparent
            dst.Pixels[12] = 0; dst.Pixels[13] = 0; dst.Pixels[14] = 0; dst.Pixels[15] = 128; // half black
            var src = new Image8(4, 1);
            src.Pixels[0] = 200; src.Pixels[3] = 0;                                            // transparent: no change
            src.Pixels[4] = 1; src.Pixels[5] = 2; src.Pixels[6] = 3; src.Pixels[7] = 255;      // opaque: replaces
            src.Pixels[8] = 40; src.Pixels[9] = 50; src.Pixels[10] = 60; src.Pixels[11] = 77;  // over nothing: itself
            src.Pixels[12] = 255; src.Pixels[13] = 255; src.Pixels[14] = 255; src.Pixels[15] = 128; // half white over half black
            ImageOps.CompositeOver(dst, src, 0, 0);
            Assert.That(Pixel(dst, 0, 0), Is.EqualTo(new byte[] { 10, 20, 30, 255 }));
            Assert.That(Pixel(dst, 1, 0), Is.EqualTo(new byte[] { 1, 2, 3, 255 }));
            Assert.That(Pixel(dst, 2, 0), Is.EqualTo(new byte[] { 40, 50, 60, 77 }));
            // alpha 0.5 + 0.5 * 0.5 = 0.75; white weighs 0.5 of 0.75
            Assert.That(Pixel(dst, 3, 0), Is.EqualTo(new byte[] { 170, 170, 170, 192 }));
        }

        [Test]
        public void CompositeOverClipsLikeBlit()
        {
            // the per-pixel reference is Pillow's arithmetic (checked against Pillow itself below)
            CheckClipping(ImageOps.CompositeOver, (dst, src) =>
            {
                var d = new Image8(1, 1, (byte[])dst.Clone());
                ImageOps.CompositeOver(d, new Image8(1, 1, src), 0, 0);
                return d.Pixels;
            });
        }

        [Test]
        public void CompositeOverMatchesPillow()
        {
            var rng = new Random(95);
            Image8 dst = TestImages.Random8(rng, 64, 48);
            Image8 src = TestImages.Random8(rng, 64, 48);
            Image8 small = TestImages.Random8(rng, 20, 30);
            using var dir = new TempDirectory();
            File.WriteAllBytes(dir.File("dst.raw"), dst.Pixels);
            File.WriteAllBytes(dir.File("src.raw"), src.Pixels);
            File.WriteAllBytes(dir.File("small.raw"), small.Pixels);
            PythonTools.Run(
                "import sys, os\n" +
                "from PIL import Image\n" +
                "d = sys.argv[1]\n" +
                "def load(name, w, h): return Image.frombytes('RGBA', (w, h), open(os.path.join(d, name), 'rb').read())\n" +
                "dst, src, small = load('dst.raw', 64, 48), load('src.raw', 64, 48), load('small.raw', 20, 30)\n" +
                "open(os.path.join(d, 'full.out'), 'wb').write(Image.alpha_composite(dst, src).tobytes())\n" +
                "dst.alpha_composite(small, (50, 30))  # crosses the right and bottom edges\n" +
                "open(os.path.join(d, 'offset.out'), 'wb').write(dst.tobytes())\n",
                dir.Path);
            Image8 full = TestImages.Copy(dst);
            ImageOps.CompositeOver(full, src, 0, 0);
            Assert.That(full.Pixels, Is.EqualTo(File.ReadAllBytes(dir.File("full.out"))));
            Image8 offset = TestImages.Copy(dst);
            ImageOps.CompositeOver(offset, small, 50, 30);
            Assert.That(offset.Pixels, Is.EqualTo(File.ReadAllBytes(dir.File("offset.out"))));
        }

        [Test]
        public void ResizeToTheSameSizeKeepsPixels()
        {
            Image8 image = TestImages.Random8(new Random(96), 13, 9);
            Image8 same = ImageOps.Resize(image, 13, 9);
            for (int i = 0; i < image.Pixels.Length; i += 4)
            {
                // fully transparent pixels come out as transparent black
                byte[] expected = image.Pixels[i + 3] == 0 ? new byte[4] : image.Pixels.Skip(i).Take(4).ToArray();
                Assert.That(same.Pixels.Skip(i).Take(4), Is.EqualTo(expected), $"pixel {i / 4}");
            }
        }

        [Test]
        public void ResizeDownByAnIntegerFactorAveragesPremultipliedBlocks()
        {
            var rng = new Random(97);
            Image8 image = TestImages.Random8(rng, 12, 9);
            Image8 small = ImageOps.Resize(image, 4, 3);
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    double[] sum = new double[4];
                    for (int dy = 0; dy < 3; dy++)
                    {
                        for (int dx = 0; dx < 3; dx++)
                        {
                            double a = image[x * 3 + dx, y * 3 + dy, 3] / 255.0;
                            for (int c = 0; c < 3; c++)
                            {
                                sum[c] += image[x * 3 + dx, y * 3 + dy, c] / 255.0 * a;
                            }
                            sum[3] += a;
                        }
                    }
                    byte alpha = ImageConvert.Quantize(sum[3] / 9);
                    byte[] expected = alpha == 0 ? new byte[4] : new[]
                    {
                        ImageConvert.Quantize(sum[0] / sum[3]), ImageConvert.Quantize(sum[1] / sum[3]),
                        ImageConvert.Quantize(sum[2] / sum[3]), alpha,
                    };
                    Assert.That(Pixel(small, x, y), Is.EqualTo(expected).AsCollection.Within(1), $"pixel ({x}, {y})");
                }
            }
        }

        [Test]
        public void ResizeUpByAnIntegerFactorRepeatsPixels()
        {
            Image8 image = TestImages.Random8(new Random(98), 5, 4);
            Image8 big = ImageOps.Resize(image, 15, 8);
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 15; x++)
                {
                    byte[] source = Pixel(image, x / 3, y / 2);
                    Assert.That(Pixel(big, x, y), Is.EqualTo(source[3] == 0 ? new byte[4] : source), $"pixel ({x}, {y})");
                }
            }
        }

        [Test]
        public void ResizeKeepsUniformImagesUniform()
        {
            Image8 image = Solid(17, 11, 200, 100, 50, 180);
            foreach ((int w, int h) in new[] { (5, 3), (7, 13), (40, 23), (1, 1), (17, 1), (64, 64) })
            {
                Image8 resized = ImageOps.Resize(image, w, h);
                Assert.That(resized.Width, Is.EqualTo(w));
                Assert.That(resized.Height, Is.EqualTo(h));
                for (int i = 0; i < resized.Pixels.Length; i += 4)
                {
                    Assert.That(resized.Pixels.Skip(i).Take(4), Is.EqualTo(new byte[] { 200, 100, 50, 180 }), $"{w}x{h}");
                }
            }
        }

        [Test]
        public void ResizeDoesNotBleedTransparentColors()
        {
            // opaque red stripes between fully transparent green ones
            var image = new Image8(10, 10);
            for (int y = 0; y < 10; y++)
            {
                for (int x = 0; x < 10; x++)
                {
                    bool red = x % 2 == 0;
                    image[x, y, 0] = red ? (byte)255 : (byte)0;
                    image[x, y, 1] = red ? (byte)0 : (byte)255;
                    image[x, y, 3] = red ? (byte)255 : (byte)0;
                }
            }
            foreach ((int w, int h) in new[] { (3, 3), (7, 4), (13, 17) })
            {
                Image8 resized = ImageOps.Resize(image, w, h);
                for (int i = 0; i < resized.Pixels.Length; i += 4)
                {
                    if (resized.Pixels[i + 3] > 0)
                    {
                        Assert.That(resized.Pixels.Skip(i).Take(3), Is.EqualTo(new byte[] { 255, 0, 0 }), $"{w}x{h}");
                    }
                }
            }
        }

        [Test]
        public void ResizeKeepsTheAverageAlpha()
        {
            var rng = new Random(99);
            for (int trial = 0; trial < 20; trial++)
            {
                Image8 image = TestImages.Random8(rng, rng.Next(1, 40), rng.Next(1, 40));
                int w = rng.Next(1, 50), h = rng.Next(1, 50);
                Image8 resized = ImageOps.Resize(image, w, h);
                double before = image.Pixels.Where((_, i) => i % 4 == 3).Average(a => a / 255.0);
                double after = resized.Pixels.Where((_, i) => i % 4 == 3).Average(a => a / 255.0);
                // area averaging conserves coverage; each output alpha is off by at most half a step
                Assert.That(after, Is.EqualTo(before).Within(0.5 / 255 + 1e-9), $"{image.Width}x{image.Height} -> {w}x{h}");
            }
        }

        [Test]
        public void ResizeRejectsBadSizes()
        {
            var image = new Image8(2, 2);
            Assert.That(() => ImageOps.Resize(image, 0, 2), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => ImageOps.Resize(image, 2, -1), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => ImageOps.Resize(null, 2, 2), Throws.ArgumentNullException);
        }
    }
}

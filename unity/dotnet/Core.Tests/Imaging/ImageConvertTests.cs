using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Imaging
{
    public class ImageConvertTests
    {
        [Test]
        public void QuantizeKnownValues()
        {
            Assert.That(ImageConvert.Quantize(0.0), Is.EqualTo(0));
            Assert.That(ImageConvert.Quantize(1.0), Is.EqualTo(255));
            Assert.That(ImageConvert.Quantize(0.5), Is.EqualTo(128)); // 127.5 + 0.5
            Assert.That(ImageConvert.Quantize(100 / 255.0), Is.EqualTo(100));
            Assert.That(ImageConvert.Quantize(100.49 / 255.0), Is.EqualTo(100));
            Assert.That(ImageConvert.Quantize(100.51 / 255.0), Is.EqualTo(101));
            Assert.That(ImageConvert.Quantize(-0.3), Is.EqualTo(0));
            Assert.That(ImageConvert.Quantize(7.0), Is.EqualTo(255));
            Assert.That(ImageConvert.Quantize(double.NaN), Is.EqualTo(0));
        }

        [Test]
        public void ToImage8UnpremultipliesAndEncodesSrgb()
        {
            var image = new ImageF(3, 1);
            image.Pixels[0] = image.Pixels[1] = image.Pixels[2] = 0.5f;
            image.Pixels[3] = 1.0f;
            // half covered: premultiplied 0.25 is straight 0.5
            image.Pixels[4] = image.Pixels[5] = image.Pixels[6] = 0.25f;
            image.Pixels[7] = 0.5f;
            // black, opaque
            image.Pixels[11] = 1.0f;
            Image8 result = ImageConvert.ToImage8(image);
            Assert.That(result.Pixels, Is.EqualTo(new byte[] { 188, 188, 188, 255, 188, 188, 188, 128, 0, 0, 0, 255 }));
        }

        [Test]
        public void ToImage8MatchesQuantizedSrgbOfTheStraightColor()
        {
            var rng = new Random(42);
            ImageF image = TestImages.RandomF(rng, 40, 30);
            for (int i = 0; i < image.Pixels.Length; i += 4)
            {
                // a valid premultiplied pixel: color at most alpha
                for (int c = 0; c < 3; c++)
                {
                    image.Pixels[i + c] *= image.Pixels[i + 3];
                }
            }
            Image8 result = ImageConvert.ToImage8(image);
            for (int i = 0; i < image.Pixels.Length; i += 4)
            {
                double alpha = image.Pixels[i + 3];
                byte alpha8 = ImageConvert.Quantize(alpha);
                Assert.That(result.Pixels[i + 3], Is.EqualTo(alpha8));
                for (int c = 0; c < 3; c++)
                {
                    byte expected = alpha8 == 0 ? (byte)0 : ImageConvert.Quantize(ColorSpace.LinearToSrgb(image.Pixels[i + c] / alpha));
                    Assert.That(result.Pixels[i + c], Is.EqualTo(expected), $"pixel {i / 4} channel {c}");
                }
            }
        }

        [Test]
        public void AlphaThatQuantizesToZeroGivesTransparentBlack()
        {
            var image = new ImageF(2, 1);
            // alpha just under half a step: invisible, whatever its color says
            image.Fill(0.001f, 0.0015f, 0.0019f, (float)(0.499 / 255));
            // just over: kept, its color unpremultiplied
            image[1, 0, 3] = (float)(0.501 / 255);
            image[1, 0, 0] = (float)(0.501 / 255);
            Image8 result = ImageConvert.ToImage8(image);
            Assert.That(result.Pixels.Take(4), Is.EqualTo(new byte[] { 0, 0, 0, 0 }));
            Assert.That(result[1, 0, 3], Is.EqualTo(1));
            Assert.That(result[1, 0, 0], Is.EqualTo(255));
        }

        [Test]
        public void ToImage8ClampsOutOfRangeValues()
        {
            var image = new ImageF(1, 1);
            // color above alpha, negative, NaN; alpha above 1
            image.Pixels[0] = 3f;
            image.Pixels[1] = -1f;
            image.Pixels[2] = float.NaN;
            image.Pixels[3] = 1.5f;
            Assert.That(ImageConvert.ToImage8(image).Pixels, Is.EqualTo(new byte[] { 255, 0, 0, 255 }));
            image.Pixels[3] = float.NaN;
            Assert.That(ImageConvert.ToImage8(image).Pixels, Is.EqualTo(new byte[] { 0, 0, 0, 0 }));
            Assert.That(() => ImageConvert.ToImage8(null), Throws.ArgumentNullException);
        }

        [Test]
        public void ShadowAlphaIsTheAverageCoverage()
        {
            // 2x supersampled 2x2 shadow: fully black, half black, a 25 % gray, white
            var silhouette = new ImageF(4, 4);
            silhouette.Fill(1, 1, 1, 1);
            void Paint(int x, int y, float r, float g, float b)
            {
                silhouette[x, y, 0] = r;
                silhouette[x, y, 1] = g;
                silhouette[x, y, 2] = b;
            }
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 2; x++)
                {
                    Paint(x, y, 0, 0, 0);
                    Paint(2 + x, 2 + y, 1, 1, 1);
                    Paint(x, 2 + y, 0.25f, 0.25f, 0.25f);
                }
                Paint(2, y, 0, 0, 0);
            }
            Image8 shadow = ImageConvert.ShadowFromSilhouette(silhouette, 2, true);
            Assert.That(shadow.Width, Is.EqualTo(2));
            Assert.That(shadow.Height, Is.EqualTo(2));
            Assert.That(shadow.Pixels, Is.EqualTo(new byte[]
            {
                0, 0, 0, 255, 0, 0, 0, 128,
                0, 0, 0, 191, 0, 0, 0, 0,
            }));
        }

        [Test]
        public void ShadowCoverageUsesTheMeanOfTheChannelsAndClamps()
        {
            var silhouette = new ImageF(3, 1);
            silhouette.Fill(1, 0, 0.5f, 0);
            silhouette[1, 0, 0] = silhouette[1, 0, 1] = silhouette[1, 0, 2] = -1f; // coverage 2 -> 1
            silhouette[2, 0, 0] = silhouette[2, 0, 1] = silhouette[2, 0, 2] = 3f; // coverage -2 -> 0
            Image8 shadow = ImageConvert.ShadowFromSilhouette(silhouette, 1, true);
            Assert.That(shadow.Pixels, Is.EqualTo(new byte[] { 0, 0, 0, 128, 0, 0, 0, 255, 0, 0, 0, 0 }));
        }

        [Test]
        public void SrgbShadowsAreDecodedBeforeCountingCoverage()
        {
            // an antialiased edge pixel 60% covered by the silhouette: linear 0.4, stored as sRGB 0.6654
            var silhouette = new ImageF(2, 1);
            silhouette.Fill(0.4f, 0.4f, 0.4f, 1);
            float srgb = (float)ColorSpace.LinearToSrgb(0.4);
            silhouette[1, 0, 0] = silhouette[1, 0, 1] = silhouette[1, 0, 2] = srgb;
            Assert.That(ImageConvert.ShadowFromSilhouette(silhouette, 1, true).Pixels[3], Is.EqualTo(153));
            Image8 decoded = ImageConvert.ShadowFromSilhouette(silhouette, 1, false);
            Assert.That(decoded.Pixels[7], Is.EqualTo(153), "sRGB 0.6654 is 60% covered");
            Assert.That(decoded.Pixels[3], Is.EqualTo(ImageConvert.Quantize(1 - ColorSpace.SrgbToLinear(0.4))));
        }

        [Test]
        public void ShadowMatchesAReferenceForRandomSilhouettes()
        {
            var rng = new Random(43);
            for (int trial = 0; trial < 10; trial++)
            {
                int ss = rng.Next(1, 5);
                ImageF silhouette = TestImages.RandomF(rng, rng.Next(1, 8) * ss, rng.Next(1, 8) * ss);
                Image8 shadow = ImageConvert.ShadowFromSilhouette(silhouette, ss, true);
                for (int y = 0; y < shadow.Height; y++)
                {
                    for (int x = 0; x < shadow.Width; x++)
                    {
                        double coverage = 0;
                        for (int dy = 0; dy < ss; dy++)
                        {
                            for (int dx = 0; dx < ss; dx++)
                            {
                                int sx = x * ss + dx, sy = y * ss + dy;
                                coverage += 1 - (silhouette[sx, sy, 0] + (double)silhouette[sx, sy, 1] + silhouette[sx, sy, 2]) / 3;
                            }
                        }
                        Assert.That(shadow[x, y, 3], Is.EqualTo(ImageConvert.Quantize(coverage / (ss * ss))));
                        Assert.That(shadow[x, y, 0] | shadow[x, y, 1] | shadow[x, y, 2], Is.EqualTo(0));
                    }
                }
            }
        }

        [Test]
        public void ShadowRejectsBadSupersampling()
        {
            Assert.That(() => ImageConvert.ShadowFromSilhouette(new ImageF(6, 6), 4, true), Throws.ArgumentException);
            Assert.That(() => ImageConvert.ShadowFromSilhouette(new ImageF(6, 6), 0, true), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => ImageConvert.ShadowFromSilhouette(null, 2, true), Throws.ArgumentNullException);
        }
    }
}

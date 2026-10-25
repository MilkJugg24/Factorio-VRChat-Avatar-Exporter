using System;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Imaging
{
    public class MattingTests
    {
        /// <summary>
        /// Renders a known straight color and alpha per pixel over black and over white the way a GPU
        /// blends (in linear light), optionally storing sRGB encoded values. Returns the expected
        /// premultiplied linear image too.
        /// </summary>
        private static (ImageF onBlack, ImageF onWhite, double[] expected) Composites(Random rng, int width, int height, bool srgb)
        {
            var onBlack = new ImageF(width, height);
            var onWhite = new ImageF(width, height);
            var expected = new double[width * height * 4];
            for (int i = 0; i < expected.Length; i += 4)
            {
                int kind = rng.Next(5);
                double alpha = kind == 0 ? 0.0 : kind == 1 ? 1.0 : rng.NextDouble();
                for (int c = 0; c < 3; c++)
                {
                    double premultiplied = rng.NextDouble() * alpha;
                    double black = premultiplied;
                    double white = premultiplied + (1 - alpha);
                    onBlack.Pixels[i + c] = (float)(srgb ? ColorSpace.LinearToSrgb(black) : black);
                    onWhite.Pixels[i + c] = (float)(srgb ? ColorSpace.LinearToSrgb(white) : white);
                    expected[i + c] = premultiplied;
                }
                expected[i + 3] = alpha;
                // whatever alpha the renderer wrote is ignored
                onBlack.Pixels[i + 3] = (float)rng.NextDouble();
                onWhite.Pixels[i + 3] = (float)rng.NextDouble();
            }
            return (onBlack, onWhite, expected);
        }

        private static void AssertMatches(ImageF actual, double[] expected, double tolerance)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual.Pixels[i], Is.EqualTo(expected[i]).Within(tolerance), $"value {i} (pixel {i / 4}, channel {i % 4})");
            }
        }

        [Test]
        public void RecoversLinearComposites()
        {
            var rng = new Random(21);
            (ImageF onBlack, ImageF onWhite, double[] expected) = Composites(rng, 37, 29, false);
            AssertMatches(Matting.FromBlackAndWhite(onBlack, onWhite, true), expected, 1e-6);
        }

        [Test]
        public void RecoversSrgbEncodedComposites()
        {
            var rng = new Random(22);
            (ImageF onBlack, ImageF onWhite, double[] expected) = Composites(rng, 41, 23, true);
            AssertMatches(Matting.FromBlackAndWhite(onBlack, onWhite, false), expected, 1e-6);
        }

        [Test]
        public void SrgbValuesAreDecodedBeforeDifferencing()
        {
            // 50 % coverage of black blends to linear 0.5 over white, stored as sRGB 0.7354
            var onBlack = new ImageF(1, 1);
            var onWhite = new ImageF(1, 1);
            onWhite.Fill((float)ColorSpace.LinearToSrgb(0.5), (float)ColorSpace.LinearToSrgb(0.5), (float)ColorSpace.LinearToSrgb(0.5), 1);
            Assert.That(Matting.FromBlackAndWhite(onBlack, onWhite, false)[0, 0, 3], Is.EqualTo(0.5).Within(1e-6));
            // read as linear, the same pixel would be much less covered
            Assert.That(Matting.FromBlackAndWhite(onBlack, onWhite, true)[0, 0, 3], Is.EqualTo(1 - 0.7354).Within(1e-4));
        }

        [Test]
        public void BackgroundOnlyIsTransparentAndIdenticalRendersAreOpaque()
        {
            var black = new ImageF(2, 1);
            var white = new ImageF(2, 1);
            white.Fill(1, 1, 1, 1);
            // pixel 1 is drawn opaque: the same color over both backgrounds
            black[1, 0, 0] = white[1, 0, 0] = 0.25f;
            black[1, 0, 1] = white[1, 0, 1] = 0.5f;
            black[1, 0, 2] = white[1, 0, 2] = 0.75f;
            foreach (bool linear in new[] { true, false })
            {
                ImageF m = Matting.FromBlackAndWhite(black, white, linear);
                Assert.That(new[] { m[0, 0, 0], m[0, 0, 1], m[0, 0, 2], m[0, 0, 3] }, Is.EqualTo(new[] { 0f, 0f, 0f, 0f }));
                Assert.That(m[1, 0, 3], Is.EqualTo(1f));
                Assert.That(m[1, 0, 1], Is.EqualTo(linear ? 0.5 : ColorSpace.SrgbToLinear(0.5)).Within(1e-7));
            }
        }

        [Test]
        public void AlphaIsOneMinusTheMeanChannelDifference()
        {
            var black = new ImageF(1, 1);
            var white = new ImageF(1, 1);
            white.Fill(0.2f, 0.4f, 0.6f, 1);
            ImageF m = Matting.FromBlackAndWhite(black, white, true);
            Assert.That(m[0, 0, 3], Is.EqualTo(0.6).Within(1e-7));
        }

        [Test]
        public void ClampsNoiseToAValidPremultipliedImage()
        {
            var black = new ImageF(3, 1);
            var white = new ImageF(3, 1);
            // white darker than black (noise): alpha clamps to 1
            black.Fill(0.6f, 0.6f, 0.6f, 1);
            white.Fill(0.5f, 0.5f, 0.5f, 1);
            // pixel 1: alpha 0.5 but a color brighter than that (HDR): the color clamps to alpha
            black[1, 0, 0] = 3f;
            black[1, 0, 1] = black[1, 0, 2] = 0f;
            white[1, 0, 0] = 3.5f;
            white[1, 0, 1] = white[1, 0, 2] = 0.5f;
            // pixel 2: negative and NaN values become 0
            black[2, 0, 0] = -0.5f;
            black[2, 0, 1] = float.NaN;
            ImageF m = Matting.FromBlackAndWhite(black, white, true);
            Assert.That(m[0, 0, 3], Is.EqualTo(1f));
            Assert.That(m[0, 0, 0], Is.EqualTo(0.6f));
            Assert.That(m[1, 0, 3], Is.EqualTo(0.5f));
            Assert.That(m[1, 0, 0], Is.EqualTo(0.5f));
            Assert.That(m[2, 0, 0], Is.EqualTo(0f));
            Assert.That(m[2, 0, 1], Is.EqualTo(0f));
        }

        [Test]
        public void LeavesTheInputsUnchanged()
        {
            var rng = new Random(23);
            ImageF black = TestImages.RandomF(rng, 5, 4);
            ImageF white = TestImages.RandomF(rng, 5, 4);
            var blackCopy = (float[])black.Pixels.Clone();
            var whiteCopy = (float[])white.Pixels.Clone();
            ImageF m = Matting.FromBlackAndWhite(black, white, false);
            Assert.That(m, Is.Not.SameAs(black).And.Not.SameAs(white));
            Assert.That(black.Pixels, Is.EqualTo(blackCopy));
            Assert.That(white.Pixels, Is.EqualTo(whiteCopy));
        }

        [Test]
        public void RejectsMismatchedOrMissingImages()
        {
            var a = new ImageF(4, 4);
            Assert.That(() => Matting.FromBlackAndWhite(a, new ImageF(4, 5), true),
                Throws.ArgumentException.With.Message.Contains("4x4").And.Message.Contains("4x5"));
            Assert.That(() => Matting.FromBlackAndWhite(a, new ImageF(3, 4), true), Throws.ArgumentException);
            Assert.That(() => Matting.FromBlackAndWhite(null, a, true), Throws.ArgumentNullException);
            Assert.That(() => Matting.FromBlackAndWhite(a, null, true), Throws.ArgumentNullException);
        }
    }
}

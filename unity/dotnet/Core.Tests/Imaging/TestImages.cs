using System;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Imaging
{
    /// <summary>Random and hand-made images for the imaging tests.</summary>
    internal static class TestImages
    {
        /// <summary>
        /// A random straight-alpha image whose alpha is a mix of fully transparent, opaque and partial
        /// values, the cases encoders and compositing treat differently.
        /// </summary>
        public static Image8 Random8(Random rng, int width, int height)
        {
            var image = new Image8(width, height);
            rng.NextBytes(image.Pixels);
            for (int i = 3; i < image.Pixels.Length; i += 4)
            {
                int kind = rng.Next(4);
                image.Pixels[i] = kind == 0 ? (byte)0 : kind == 1 ? (byte)255 : image.Pixels[i];
            }
            return image;
        }

        /// <summary>An image of smooth gradients with noise: realistic rows that make the filters differ.</summary>
        public static Image8 Smooth8(Random rng, int width, int height)
        {
            var image = new Image8(width, height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    image[x, y, 0] = (byte)(x * 3 + rng.Next(3));
                    image[x, y, 1] = (byte)(y * 5 + x);
                    image[x, y, 2] = (byte)(x * y);
                    image[x, y, 3] = (byte)(x < width / 3 ? 0 : 255 - y);
                }
            }
            return image;
        }

        public static ImageF RandomF(Random rng, int width, int height, double min = 0, double max = 1)
        {
            var image = new ImageF(width, height);
            for (int i = 0; i < image.Pixels.Length; i++)
            {
                image.Pixels[i] = (float)(min + rng.NextDouble() * (max - min));
            }
            return image;
        }

        public static Image8 Copy(Image8 image) => new Image8(image.Width, image.Height, (byte[])image.Pixels.Clone());

        public static void AssertEqual(Image8 actual, Image8 expected, string message = "")
        {
            Assert.That(actual.Width, Is.EqualTo(expected.Width), "width " + message);
            Assert.That(actual.Height, Is.EqualTo(expected.Height), "height " + message);
            for (int y = 0; y < expected.Height; y++)
            {
                for (int x = 0; x < expected.Width; x++)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        if (actual[x, y, c] != expected[x, y, c])
                        {
                            Assert.Fail($"pixel ({x}, {y}) channel {c}: {actual[x, y, c]} instead of {expected[x, y, c]} {message}");
                        }
                    }
                }
            }
        }
    }
}

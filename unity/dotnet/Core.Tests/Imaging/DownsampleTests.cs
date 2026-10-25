using System;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Imaging
{
    public class DownsampleTests
    {
        [Test]
        public void AveragesEachBlockInEveryChannel()
        {
            var image = new ImageF(4, 2);
            // left block: four different pixels, right block: one opaque pixel among transparent ones
            image.Pixels[image.Index(0, 0)] = 0.2f;
            image.Pixels[image.Index(1, 0) + 1] = 0.4f;
            image.Pixels[image.Index(0, 1) + 2] = 0.8f;
            image.Pixels[image.Index(1, 1) + 3] = 1.0f;
            for (int c = 0; c < 4; c++)
            {
                image[3, 1, c] = 1.0f;
            }
            ImageF small = Downsample.Box(image, 2);
            Assert.That(small.Width, Is.EqualTo(2));
            Assert.That(small.Height, Is.EqualTo(1));
            Assert.That(new[] { small[0, 0, 0], small[0, 0, 1], small[0, 0, 2], small[0, 0, 3] },
                Is.EqualTo(new[] { 0.05f, 0.1f, 0.2f, 0.25f }).Within(1e-7));
            Assert.That(new[] { small[1, 0, 0], small[1, 0, 1], small[1, 0, 2], small[1, 0, 3] },
                Is.EqualTo(new[] { 0.25f, 0.25f, 0.25f, 0.25f }).Within(1e-7));
        }

        [Test]
        public void MatchesAReferenceAverageForRandomImages()
        {
            var rng = new Random(31);
            for (int trial = 0; trial < 30; trial++)
            {
                int factor = rng.Next(1, 5);
                int width = rng.Next(1, 9) * factor;
                int height = rng.Next(1, 9) * factor;
                ImageF image = TestImages.RandomF(rng, width, height, -0.5, 2.0);
                ImageF small = Downsample.Box(image, factor);
                Assert.That(small.Width, Is.EqualTo(width / factor));
                Assert.That(small.Height, Is.EqualTo(height / factor));
                for (int y = 0; y < small.Height; y++)
                {
                    for (int x = 0; x < small.Width; x++)
                    {
                        for (int c = 0; c < 4; c++)
                        {
                            double sum = 0;
                            for (int dy = 0; dy < factor; dy++)
                            {
                                for (int dx = 0; dx < factor; dx++)
                                {
                                    sum += image[x * factor + dx, y * factor + dy, c];
                                }
                            }
                            Assert.That(small[x, y, c], Is.EqualTo(sum / (factor * factor)).Within(1e-6));
                        }
                    }
                }
            }
        }

        [Test]
        public void FactorOneReturnsACopy()
        {
            ImageF image = TestImages.RandomF(new Random(32), 3, 5);
            ImageF copy = Downsample.Box(image, 1);
            Assert.That(copy, Is.Not.SameAs(image));
            Assert.That(copy.Pixels, Is.Not.SameAs(image.Pixels));
            Assert.That(copy.Pixels, Is.EqualTo(image.Pixels));
        }

        [Test]
        public void RejectsSizesThatAreNotMultiples()
        {
            Assert.That(() => Downsample.Box(new ImageF(6, 5), 2),
                Throws.ArgumentException.With.Message.Contains("6x5").And.Message.Contains("2"));
            Assert.That(() => Downsample.Box(new ImageF(5, 6), 2), Throws.ArgumentException);
            Assert.That(() => Downsample.Box(new ImageF(4, 4), 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => Downsample.Box(new ImageF(4, 4), -2), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => Downsample.Box(null, 2), Throws.ArgumentNullException);
        }
    }
}

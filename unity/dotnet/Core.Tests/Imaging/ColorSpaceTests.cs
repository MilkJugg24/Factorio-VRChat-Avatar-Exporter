using System;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Imaging
{
    public class ColorSpaceTests
    {
        [Test]
        public void KnownValues()
        {
            Assert.That(ColorSpace.LinearToSrgb(0.5), Is.EqualTo(0.7353569830524495).Within(1e-15));
            Assert.That(ColorSpace.SrgbToLinear(0.5), Is.EqualTo(0.21404114048223255).Within(1e-15));
            Assert.That(ColorSpace.LinearToSrgb(0.0), Is.EqualTo(0.0));
            Assert.That(ColorSpace.SrgbToLinear(0.0), Is.EqualTo(0.0));
            Assert.That(ColorSpace.LinearToSrgb(1.0), Is.EqualTo(1.0).Within(1e-15));
            Assert.That(ColorSpace.SrgbToLinear(1.0), Is.EqualTo(1.0).Within(1e-15));
        }

        [Test]
        public void DarkValuesUseTheLinearSegment()
        {
            Assert.That(ColorSpace.LinearToSrgb(0.002), Is.EqualTo(0.002 * 12.92).Within(1e-17));
            Assert.That(ColorSpace.SrgbToLinear(0.04), Is.EqualTo(0.04 / 12.92).Within(1e-17));
        }

        [Test]
        public void SegmentsMeetAtTheThresholds()
        {
            // the two pieces of each curve agree (to the standard's rounding) where they switch
            Assert.That(ColorSpace.LinearToSrgb(0.0031308), Is.EqualTo(0.04045).Within(1e-6));
            Assert.That(ColorSpace.LinearToSrgb(0.0031308 + 1e-12), Is.EqualTo(0.04045).Within(1e-6));
            Assert.That(ColorSpace.SrgbToLinear(0.04045), Is.EqualTo(0.0031308).Within(1e-6));
            Assert.That(ColorSpace.SrgbToLinear(0.04045 + 1e-12), Is.EqualTo(0.0031308).Within(1e-6));
        }

        [Test]
        public void ClampsOutOfRangeAndNaN()
        {
            foreach (Func<double, double> f in new Func<double, double>[] { ColorSpace.LinearToSrgb, ColorSpace.SrgbToLinear })
            {
                Assert.That(f(-0.5), Is.EqualTo(0.0));
                Assert.That(f(double.NegativeInfinity), Is.EqualTo(0.0));
                Assert.That(f(double.NaN), Is.EqualTo(0.0));
                Assert.That(f(1.5), Is.EqualTo(f(1.0)));
                Assert.That(f(double.PositiveInfinity), Is.EqualTo(f(1.0)));
            }
            Assert.That(ColorSpace.Clamp01(0.25), Is.EqualTo(0.25));
            Assert.That(ColorSpace.Clamp01(-0.25), Is.EqualTo(0.0));
            Assert.That(ColorSpace.Clamp01(1.25), Is.EqualTo(1.0));
            Assert.That(ColorSpace.Clamp01(double.NaN), Is.EqualTo(0.0));
        }

        [Test]
        public void CurvesAreInverses()
        {
            var rng = new Random(11);
            for (int i = 0; i < 10000; i++)
            {
                double v = rng.NextDouble();
                Assert.That(ColorSpace.SrgbToLinear(ColorSpace.LinearToSrgb(v)), Is.EqualTo(v).Within(1e-12));
                Assert.That(ColorSpace.LinearToSrgb(ColorSpace.SrgbToLinear(v)), Is.EqualTo(v).Within(1e-12));
            }
        }

        [Test]
        public void CurvesAreIncreasingAndStayInRange()
        {
            double previousEncoded = -1;
            double previousDecoded = -1;
            for (int i = 0; i <= 100000; i++)
            {
                double v = i / 100000.0;
                double encoded = ColorSpace.LinearToSrgb(v);
                double decoded = ColorSpace.SrgbToLinear(v);
                // increasing, in range, and encoding brightens while decoding darkens
                bool ok = encoded > previousEncoded && decoded > previousDecoded && encoded <= 1 && decoded >= 0
                    && encoded >= v - 1e-15 && decoded <= v + 1e-15;
                if (!ok)
                {
                    Assert.Fail($"at {v:R}: LinearToSrgb {encoded:R} (previous {previousEncoded:R}), SrgbToLinear {decoded:R} (previous {previousDecoded:R})");
                }
                previousEncoded = encoded;
                previousDecoded = decoded;
            }
        }
    }
}

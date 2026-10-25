using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Pack
{
    public class DecimalRoundingTests
    {
        [TestCase(0.015625, 5, 0.01562)] // exact tie: to even
        [TestCase(0.046875, 5, 0.04688)]
        [TestCase(-0.015625, 5, -0.01562)]
        [TestCase(0.640625, 5, 0.64062)]
        [TestCase(2.5, 0, 2.0)]
        [TestCase(3.5, 0, 4.0)]
        [TestCase(-1.03125, 5, -1.03125)]
        [TestCase(0.123456, 5, 0.12346)]
        [TestCase(1.0000049999, 5, 1.0)]
        [TestCase(2.675, 2, 2.67)] // 2.675 is stored as 2.67499999...
        [TestCase(1e20, 5, 1e20)]
        [TestCase(123456.5, 0, 123456.0)]
        public void RoundsLikePython(double value, int digits, double expected)
        {
            Assert.That(DecimalRounding.Round(value, digits), Is.EqualTo(expected));
        }

        [Test]
        public void KeepsTheSignOfZero()
        {
            Assert.That(BitConverter.DoubleToInt64Bits(DecimalRounding.Round(-1e-7, 5)), Is.EqualTo(BitConverter.DoubleToInt64Bits(-0.0)));
            Assert.That(BitConverter.DoubleToInt64Bits(DecimalRounding.Round(-0.0, 5)), Is.EqualTo(BitConverter.DoubleToInt64Bits(-0.0)));
            Assert.That(BitConverter.DoubleToInt64Bits(DecimalRounding.Round(1e-7, 5)), Is.EqualTo(0L));
        }

        [Test]
        public void LeavesNonFiniteValuesAndChecksDigits()
        {
            Assert.That(DecimalRounding.Round(double.PositiveInfinity, 5), Is.EqualTo(double.PositiveInfinity));
            Assert.That(double.IsNaN(DecimalRounding.Round(double.NaN, 5)));
            Assert.Throws<ArgumentOutOfRangeException>(() => DecimalRounding.Round(1.0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => DecimalRounding.Round(1.0, DecimalRounding.MaxDigits + 1));
        }

        [Test]
        public void MatchesPythonOnManyValues()
        {
            var rng = new Random(3);
            var values = new List<(double value, int digits)>();
            for (int i = 0; i < 4000; i++)
            {
                switch (i % 6)
                {
                    case 0: // shifts at 64 and 32 pixels per tile: half pixels over a power of two
                        values.Add((rng.Next(-4000, 4000) / 2.0 / (i % 12 == 0 ? 64 : 32), 5));
                        break;
                    case 1: // other pixels per tile
                        values.Add((rng.Next(-4000, 4000) / 2.0 / rng.Next(1, 200), 5));
                        break;
                    case 2:
                        values.Add(((rng.NextDouble() - 0.5) * 20, rng.Next(0, 10)));
                        break;
                    case 3: // large values, where only some digits remain
                        values.Add(((rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(5, 20)), rng.Next(0, 16)));
                        break;
                    case 4: // tiny values and subnormals
                        values.Add(((rng.NextDouble() - 0.5) * Math.Pow(10, -rng.Next(1, 320)), rng.Next(0, 16)));
                        break;
                    default: // any finite bit pattern
                        double v;
                        do
                        {
                            v = BitConverter.Int64BitsToDouble((long)(rng.NextDouble() * long.MaxValue) * (rng.Next(2) == 0 ? 1 : -1));
                        }
                        while (double.IsNaN(v) || double.IsInfinity(v));
                        values.Add((v, rng.Next(0, 16)));
                        break;
                }
            }
            values.Add((Math.Pow(2, -20), 15)); // powers of two with the most digits
            values.Add((Math.Pow(2, 40) + 0.5, 15));
            string input = JsonSerializer.Serialize(values.Select(v => new[] { BitConverter.DoubleToInt64Bits(v.value), v.digits }));
            const string script = @"
import json, struct
pairs = json.load(sys.stdin)
out = []
for bits, digits in pairs:
    x = struct.unpack('<d', struct.pack('<q', bits))[0]
    out.append(struct.unpack('<q', struct.pack('<d', round(x, digits)))[0])
print(json.dumps(out))
";
            long[] python = JsonSerializer.Deserialize<long[]>(PythonTools.Run(script, input));
            for (int i = 0; i < values.Count; i++)
            {
                double mine = DecimalRounding.Round(values[i].value, values[i].digits);
                Assert.That(BitConverter.DoubleToInt64Bits(mine), Is.EqualTo(python[i]),
                    $"round({values[i].value:R}, {values[i].digits}): {mine:R} vs {BitConverter.Int64BitsToDouble(python[i]):R}");
            }
        }
    }
}

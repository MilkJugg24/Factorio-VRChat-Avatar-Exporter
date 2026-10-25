using System;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Imaging
{
    public class ChecksumTests
    {
        // Adler-32 straight from RFC 1950, reducing after every byte.
        private static uint SlowAdler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte x in data)
            {
                a = (a + x) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        [Test]
        public void Crc32CheckValue()
        {
            Assert.That(Checksums.Crc32(Encoding.ASCII.GetBytes("123456789")), Is.EqualTo(0xCBF43926u));
            Assert.That(Checksums.Crc32(Array.Empty<byte>()), Is.EqualTo(0u));
            // the CRC of an IEND chunk (type, no data), as found in every PNG
            Assert.That(Checksums.Crc32(Encoding.ASCII.GetBytes("IEND")), Is.EqualTo(0xAE426082u));
        }

        [Test]
        public void Adler32CheckValue()
        {
            Assert.That(Checksums.Adler32(Encoding.ASCII.GetBytes("Wikipedia")), Is.EqualTo(0x11E60398u));
            Assert.That(Checksums.Adler32(Array.Empty<byte>()), Is.EqualTo(1u));
        }

        [Test]
        public void MatchReferenceImplementationsOnRandomData()
        {
            var rng = new Random(51);
            foreach (int length in new[] { 1, 2, 3, 100, 5551, 5552, 5553, 20000 })
            {
                var data = new byte[length];
                rng.NextBytes(data);
                Assert.That(Checksums.Crc32(data), Is.EqualTo(TestPng.BitwiseCrc32(data)), $"CRC-32 of {length} bytes");
                Assert.That(Checksums.Adler32(data), Is.EqualTo(SlowAdler32(data)), $"Adler-32 of {length} bytes");
            }
        }

        [Test]
        public void Adler32SumsDoNotOverflowOnLongRunsOfFF()
        {
            // the worst case for the deferred modulo: every byte as large as possible
            byte[] data = Enumerable.Repeat((byte)0xFF, 100000).ToArray();
            Assert.That(Checksums.Adler32(data), Is.EqualTo(SlowAdler32(data)));
        }

        [Test]
        public void RangesMatchCopies()
        {
            var rng = new Random(52);
            var data = new byte[300];
            rng.NextBytes(data);
            for (int trial = 0; trial < 50; trial++)
            {
                int offset = rng.Next(0, 301);
                int count = rng.Next(0, 301 - offset);
                byte[] slice = data.Skip(offset).Take(count).ToArray();
                Assert.That(Checksums.Crc32(data, offset, count), Is.EqualTo(Checksums.Crc32(slice)));
                Assert.That(Checksums.Adler32(data, offset, count), Is.EqualTo(Checksums.Adler32(slice)));
            }
        }

        [Test]
        public void UpdateCrc32ContinuesACrc()
        {
            var rng = new Random(53);
            var data = new byte[200];
            rng.NextBytes(data);
            for (int split = 0; split <= data.Length; split += 7)
            {
                uint first = Checksums.Crc32(data, 0, split);
                Assert.That(Checksums.UpdateCrc32(first, data, split, data.Length - split), Is.EqualTo(Checksums.Crc32(data)));
            }
        }

        [Test]
        public void RejectBadRanges()
        {
            var data = new byte[10];
            Assert.That(() => Checksums.Crc32(data, -1, 2), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => Checksums.Crc32(data, 5, 6), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => Checksums.Adler32(data, 0, -1), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => Checksums.Adler32(data, 11, 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => Checksums.UpdateCrc32(0, data, int.MaxValue, 1), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => Checksums.Crc32(null), Throws.ArgumentNullException);
            Assert.That(() => Checksums.Adler32(null), Throws.ArgumentNullException);
            Assert.That(() => Checksums.Adler32(null, 0, 0), Throws.ArgumentNullException);
        }
    }
}

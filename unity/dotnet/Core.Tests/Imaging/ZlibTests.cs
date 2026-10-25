using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Imaging
{
    public class ZlibTests
    {
        private static byte[] Sample(Random rng, int length, bool compressible)
        {
            var data = new byte[length];
            rng.NextBytes(data);
            if (compressible)
            {
                for (int i = 0; i < length; i++)
                {
                    data[i] = (byte)(data[i] % 4 + i / 1000);
                }
            }
            return data;
        }

        [Test]
        public void RoundTrips()
        {
            var rng = new Random(61);
            foreach (int length in new[] { 0, 1, 2, 1000, 70000, 300000 })
            {
                foreach (bool compressible in new[] { false, true })
                {
                    byte[] data = Sample(rng, length, compressible);
                    byte[] z = Zlib.Compress(data);
                    Assert.That(Zlib.Decompress(z, length), Is.EqualTo(data), $"{length} bytes");
                    Assert.That(TestPng.ZlibDecompress(z), Is.EqualTo(data), $"{length} bytes, by ZLibStream");
                }
            }
        }

        [Test]
        public void HasAValidHeaderAndTheAdlerTrailer()
        {
            byte[] data = Sample(new Random(62), 5000, true);
            byte[] z = Zlib.Compress(data);
            Assert.That(z[0], Is.EqualTo(0x78), "deflate, 32 KiB window");
            Assert.That((z[0] * 256 + z[1]) % 31, Is.EqualTo(0), "check bits");
            Assert.That(z[1] & 0x20, Is.EqualTo(0), "no preset dictionary");
            Assert.That(TestPng.ReadUInt32(z, z.Length - 4), Is.EqualTo(Checksums.Adler32(data)));
            Assert.That(z.Length, Is.LessThan(data.Length / 2), "compresses");
        }

        [Test]
        public void IsDeterministic()
        {
            byte[] data = Sample(new Random(63), 100000, true);
            Assert.That(Zlib.Compress(data), Is.EqualTo(Zlib.Compress((byte[])data.Clone())));
        }

        [Test]
        public void CompressesEmptyInputToAnEmptyFinalBlock()
        {
            // Mono's DeflateStream writes no block at all for empty input; .NET writes this one
            byte[] z = Zlib.Compress(Array.Empty<byte>());
            Assert.That(BitConverter.ToString(z), Is.EqualTo("78-9C-03-00-00-00-00-01"));
            Assert.That(Zlib.Decompress(z, 0), Is.Empty);
        }

        [Test]
        public void InteroperatesWithPythonZlib()
        {
            var rng = new Random(64);
            using var dir = new TempDirectory();
            byte[] data = Sample(rng, 50000, true);
            byte[] empty = Array.Empty<byte>();
            File.WriteAllBytes(dir.File("data"), data);
            File.WriteAllBytes(dir.File("ours.z"), Zlib.Compress(data));
            File.WriteAllBytes(dir.File("empty.z"), Zlib.Compress(empty));
            PythonTools.Run(
                "import sys, zlib, os\n" +
                "d = sys.argv[1]\n" +
                "data = open(os.path.join(d, 'data'), 'rb').read()\n" +
                "assert zlib.decompress(open(os.path.join(d, 'ours.z'), 'rb').read()) == data\n" +
                "assert zlib.decompress(open(os.path.join(d, 'empty.z'), 'rb').read()) == b''\n" +
                "for level in (0, 1, 6, 9):\n" +
                "    open(os.path.join(d, f'python{level}.z'), 'wb').write(zlib.compress(data, level))\n",
                dir.Path);
            foreach (int level in new[] { 0, 1, 6, 9 })
            {
                Assert.That(Zlib.Decompress(File.ReadAllBytes(dir.File($"python{level}.z")), data.Length), Is.EqualTo(data), $"level {level}");
            }
        }

        [Test]
        public void RejectsBadHeaders()
        {
            byte[] good = Zlib.Compress(new byte[] { 1, 2, 3 });
            byte[] Patched(byte cmf, byte flg)
            {
                var z = (byte[])good.Clone();
                z[0] = cmf;
                z[1] = flg;
                return z;
            }
            // method 7, window code 8, broken check bits, a preset dictionary (the others' check bits are valid)
            Assert.That(() => Zlib.Decompress(Patched(0x77, 0x09), 3),
                Throws.InstanceOf<InvalidDataException>().With.Message.Contains("not deflate"));
            Assert.That(() => Zlib.Decompress(Patched(0x88, 0x1C), 3),
                Throws.InstanceOf<InvalidDataException>().With.Message.Contains("window"));
            Assert.That(() => Zlib.Decompress(Patched(0x78, 0x9D), 3),
                Throws.InstanceOf<InvalidDataException>().With.Message.Contains("check bits"));
            Assert.That(() => Zlib.Decompress(Patched(0x78, 0xBB), 3),
                Throws.InstanceOf<InvalidDataException>().With.Message.Contains("dictionary"));
            Assert.That(() => Zlib.Decompress(new byte[] { 0x78, 0x9C, 0x03 }, 3),
                Throws.InstanceOf<InvalidDataException>().With.Message.Contains("too short"));
        }

        [Test]
        public void RejectsCorruptData()
        {
            byte[] data = Sample(new Random(65), 2000, false);
            byte[] z = Zlib.Compress(data);
            var wrongAdler = (byte[])z.Clone();
            wrongAdler[^1] ^= 1;
            Assert.That(() => Zlib.Decompress(wrongAdler, data.Length),
                Throws.InstanceOf<InvalidDataException>().With.Message.Contains("Adler-32"));
            // a stored block whose length and its complement disagree
            byte[] badDeflate = { 0x78, 0x9C, 0x01, 0x05, 0x00, 0x00, 0x00, 1, 2, 3, 4, 5, 0, 0, 0, 0 };
            Assert.That(() => Zlib.Decompress(badDeflate, 100),
                Throws.InstanceOf<InvalidDataException>().With.Message.Contains("corrupt"));
            // a truncated stream gives too little data, caught by the checksum
            byte[] truncated = z.Take(z.Length / 2).Concat(z.Skip(z.Length - 4)).ToArray();
            Assert.That(() => Zlib.Decompress(truncated, data.Length), Throws.InstanceOf<InvalidDataException>());
        }

        [Test]
        public void EnforcesTheLengthLimit()
        {
            byte[] data = new byte[100000];
            byte[] z = Zlib.Compress(data);
            Assert.That(Zlib.Decompress(z, data.Length), Has.Length.EqualTo(data.Length));
            Assert.That(() => Zlib.Decompress(z, data.Length - 1),
                Throws.InstanceOf<InvalidDataException>().With.Message.Contains("more than"));
            Assert.That(() => Zlib.Decompress(z, -1), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => Zlib.Decompress(null, 1), Throws.ArgumentNullException);
            Assert.That(() => Zlib.Compress(null), Throws.ArgumentNullException);
        }
    }
}

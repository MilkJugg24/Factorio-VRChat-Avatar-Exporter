using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Pack
{
    public class PackOutputTests
    {
        private static readonly byte[] Hello = Encoding.UTF8.GetBytes("hello");

        [Test]
        public void DirectoryOutputWritesThePackFolder()
        {
            using (var temp = new TempDirectory())
            {
                var output = new DirectoryPackOutput(temp.Path, false);
                output.Begin("mika", "1.0.0");
                output.Write("info.json", Hello);
                output.Write("graphics/idle-base.png", new byte[] { 1, 2, 3 });
                output.Complete();
                string folder = Path.Combine(temp.Path, "vrc-avatar-mika");
                Assert.That(output.PackDirectory, Is.EqualTo(folder));
                Assert.That(File.ReadAllBytes(Path.Combine(folder, "info.json")), Is.EqualTo(Hello));
                Assert.That(File.ReadAllBytes(Path.Combine(folder, "graphics", "idle-base.png")), Is.EqualTo(new byte[] { 1, 2, 3 }));
            }
        }

        [Test]
        public void DirectoryOutputRefusesAnExistingFolderUnlessOverwriting()
        {
            using (var temp = new TempDirectory())
            {
                string folder = Path.Combine(temp.Path, "vrc-avatar-mika");
                Directory.CreateDirectory(Path.Combine(folder, "graphics"));
                File.WriteAllText(Path.Combine(folder, "graphics", "stale.png"), "old");

                Assert.Throws<IOException>(() => new DirectoryPackOutput(temp.Path, false).Begin("mika", "1.0.0"));
                Assert.That(File.Exists(Path.Combine(folder, "graphics", "stale.png")), "left alone");

                var output = new DirectoryPackOutput(temp.Path, true);
                output.Begin("mika", "1.0.0");
                output.Write("info.json", Hello);
                output.Complete();
                Assert.That(Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(Path.GetFileName), Is.EqualTo(new[] { "info.json" }));

                File.WriteAllText(Path.Combine(temp.Path, "vrc-avatar-file"), "not a folder");
                Assert.Throws<IOException>(() => new DirectoryPackOutput(temp.Path, true).Begin("file", "1.0.0"));
            }
        }

        [Test]
        public void DirectoryOutputChecksItsInput()
        {
            Assert.Throws<ArgumentException>(() => new DirectoryPackOutput("", false));
            using (var temp = new TempDirectory())
            {
                Assert.Throws<DirectoryNotFoundException>(() => new DirectoryPackOutput(Path.Combine(temp.Path, "missing"), false).Begin("mika", "1.0.0"));
                Assert.Throws<ArgumentException>(() => new DirectoryPackOutput(temp.Path, false).Begin("Mika", "1.0.0"));
                var output = new DirectoryPackOutput(temp.Path, false);
                Assert.Throws<InvalidOperationException>(() => output.Write("info.json", Hello), "before Begin");
                output.Begin("mika", "1.0.0");
                Assert.Throws<InvalidOperationException>(() => output.Begin("mika", "1.0.0"));
                AssertRefusesBadFiles(output);
                Assert.That(Directory.GetFiles(temp.Path, "*", SearchOption.AllDirectories).Select(Path.GetFileName),
                    Is.EqualTo(new[] { "dup.txt" }), "nothing written outside the pack folder");
                output.Complete();
                Assert.Throws<InvalidOperationException>(() => output.Write("late.txt", Hello));
                Assert.Throws<InvalidOperationException>(() => output.Complete());
            }
        }

        private static void AssertRefusesBadFiles(IPackOutput output)
        {
            foreach (string path in new[] { "", "/info.json", "a//b", "../escape.txt", "graphics/../../x", "./a", "a\\b", "c:x", "a/", "a\nb" })
            {
                Assert.Throws<ArgumentException>(() => output.Write(path, Hello), path);
            }
            Assert.Throws<ArgumentNullException>(() => output.Write("x.txt", null));
            output.Write("dup.txt", Hello);
            Assert.Throws<ArgumentException>(() => output.Write("dup.txt", Hello));
        }

        private static byte[] Zip(Action<ZipPackOutput> write)
        {
            using (var stream = new MemoryStream())
            {
                var output = new ZipPackOutput(stream);
                write(output);
                return stream.ToArray();
            }
        }

        private static void WriteSample(ZipPackOutput output)
        {
            output.Begin("mika", "1.2.3");
            output.Write("prototypes/avatar-pack.lua", Encoding.UTF8.GetBytes("return {}\n"));
            output.Write("info.json", Hello);
            output.Write("graphics/idle-base.png", Enumerable.Range(0, 5000).Select(i => (byte)(i % 7)).ToArray());
            output.Write("README.txt", Encoding.UTF8.GetBytes("read me\n"));
            output.Complete();
        }

        [Test]
        public void ZipHoldsOneFolderInOrdinalOrderWithFixedTimes()
        {
            byte[] zip = Zip(WriteSample);
            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                Assert.That(archive.Entries.Select(e => e.FullName), Is.EqualTo(new[]
                {
                    "vrc-avatar-mika_1.2.3/README.txt",
                    "vrc-avatar-mika_1.2.3/graphics/idle-base.png",
                    "vrc-avatar-mika_1.2.3/info.json",
                    "vrc-avatar-mika_1.2.3/prototypes/avatar-pack.lua",
                }));
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    Assert.That(entry.LastWriteTime.DateTime, Is.EqualTo(new DateTime(2000, 1, 1, 0, 0, 0)), entry.FullName);
                }
                using (var reader = new MemoryStream())
                {
                    archive.GetEntry("vrc-avatar-mika_1.2.3/info.json").Open().CopyTo(reader);
                    Assert.That(reader.ToArray(), Is.EqualTo(Hello));
                }
                ZipArchiveEntry png = archive.GetEntry("vrc-avatar-mika_1.2.3/graphics/idle-base.png");
                Assert.That(png.CompressedLength, Is.LessThan(png.Length), "compressed");
            }
        }

        [Test]
        public void ZipIsDeterministic()
        {
            byte[] first = Zip(WriteSample);
            byte[] second = Zip(WriteSample);
            Assert.That(second, Is.EqualTo(first));
            // the order of the writes does not matter either
            byte[] reordered = Zip(output =>
            {
                output.Begin("mika", "1.2.3");
                output.Write("README.txt", Encoding.UTF8.GetBytes("read me\n"));
                output.Write("graphics/idle-base.png", Enumerable.Range(0, 5000).Select(i => (byte)(i % 7)).ToArray());
                output.Write("info.json", Hello);
                output.Write("prototypes/avatar-pack.lua", Encoding.UTF8.GetBytes("return {}\n"));
                output.Complete();
            });
            Assert.That(reordered, Is.EqualTo(first));
        }

        [Test]
        public void ZipWritesNothingUntilCompleteAndLeavesTheStreamOpen()
        {
            using (var stream = new MemoryStream())
            {
                var output = new ZipPackOutput(stream);
                output.Begin("mika", "1.0.0");
                output.Write("info.json", Hello);
                Assert.That(stream.Length, Is.EqualTo(0));
                output.Complete();
                Assert.That(stream.Length, Is.GreaterThan(0));
                Assert.That(stream.CanWrite, "still open");
            }
        }

        [Test]
        public void ZipOutputChecksItsInput()
        {
            Assert.Throws<ArgumentNullException>(() => new ZipPackOutput(null));
            Assert.Throws<ArgumentException>(() => new ZipPackOutput(new MemoryStream(new byte[4], false)));
            var output = new ZipPackOutput(new MemoryStream());
            Assert.Throws<ArgumentException>(() => output.Begin("mika", "1.0"));
            Assert.Throws<ArgumentException>(() => output.Begin("corpse-mika", "1.0.0"));
            Assert.Throws<InvalidOperationException>(() => output.Write("info.json", Hello), "before Begin");
            output.Begin("mika", "1.0.0");
            Assert.Throws<InvalidOperationException>(() => output.Begin("mika", "1.0.0"));
            AssertRefusesBadFiles(output);
            output.Complete();
            Assert.Throws<InvalidOperationException>(() => output.Complete());
        }
    }
}

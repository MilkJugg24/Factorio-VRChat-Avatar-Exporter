using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace MilkJugg24.FactorioAvatarExporter.Core.Tests.Pack
{
    public class PackBundlerTests
    {
        /// <summary>A pack's zip, as the exporter writes it.</summary>
        private static byte[] PackZip(string id, string title, string version, bool flight = false)
        {
            PackContent content = SyntheticPack.Build(flight, false, 64, id);
            content.Title = title;
            content.Version = version;
            using (var stream = new MemoryStream())
            {
                new PackWriter(PngWriter.Encode).Write(content, new ZipPackOutput(stream));
                return stream.ToArray();
            }
        }

        private static BundleInput Read(byte[] zip, string source)
        {
            using (var stream = new MemoryStream(zip))
            {
                return PackBundler.Read(stream, source);
            }
        }

        private static byte[] Bundle(BundleInfo info, IReadOnlyList<BundleCharacter> characters)
        {
            using (var stream = new MemoryStream())
            {
                PackBundler.Write(stream, info, characters);
                return stream.ToArray();
            }
        }

        private static Dictionary<string, byte[]> Entries(byte[] zip)
        {
            var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    using (Stream s = entry.Open())
                    using (var copy = new MemoryStream())
                    {
                        s.CopyTo(copy);
                        entries.Add(entry.FullName, copy.ToArray());
                    }
                }
            }
            return entries;
        }

        private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

        private static BundleInfo Info(string version = "1.0.0") => new BundleInfo
        {
            Id = "my-server", Title = "My Server", Version = version, Author = "Owner", GeneratorVersion = "0.4.0",
        };

        [Test]
        public void ReadsAPack()
        {
            BundleInput input = Read(PackZip("mika", "Mika \"M\" ミカ", "1.2.3"), "mika.zip");
            Assert.That(input.ModName, Is.EqualTo("avatar-pack-mika"));
            Assert.That(input.IsBundle, Is.False);
            BundleCharacter mika = input.Characters.Single();
            Assert.That(mika.Id, Is.EqualTo("mika"));
            Assert.That(mika.Title, Is.EqualTo("Mika \"M\" ミカ"));
            Assert.That(mika.Author, Is.EqualTo("Tester"));
            Assert.That(mika.Version, Is.EqualTo("1.2.3"));
            Assert.That(mika.Source, Is.EqualTo("mika.zip"));
        }

        [Test]
        public void WritesABundleThatPointsIntoItself()
        {
            List<BundleCharacter> characters = PackBundler.Merge(new[]
            {
                Read(PackZip("mika", "Mika", "1.0.0", flight: true), "mika.zip"),
                Read(PackZip("draco", "Draco", "2.0.0"), "draco.zip"),
            }, null);
            Dictionary<string, byte[]> entries = Entries(Bundle(Info("1.0.3"), characters));
            const string root = "avatar-bundle-my-server_1.0.3/";
            Assert.That(entries.Keys.All(k => k.StartsWith(root, StringComparison.Ordinal)), Is.True);

            Assert.That(Text(entries[root + "data.lua"]), Is.EqualTo(PackBundler.DataLua("my-server", new[] { "draco", "mika" })));
            using (JsonDocument info = JsonDocument.Parse(entries[root + "info.json"]))
            {
                JsonElement json = info.RootElement;
                Assert.That(json.GetProperty("name").GetString(), Is.EqualTo("avatar-bundle-my-server"));
                Assert.That(json.GetProperty("version").GetString(), Is.EqualTo("1.0.3"));
                Assert.That(json.GetProperty("title").GetString(), Is.EqualTo("Avatar bundle: My Server"));
                Assert.That(json.GetProperty("factorio_version").GetString(), Is.EqualTo(PackFormat.FactorioVersion));
                Assert.That(json.GetProperty("dependencies").EnumerateArray().Single().GetString(), Is.EqualTo("avatar-characters >= 0.5.0"));
                StringAssert.Contains("Draco, Mika", json.GetProperty("description").GetString());
            }
            foreach (string id in new[] { "draco", "mika" })
            {
                string folder = root + "characters/" + id + "/";
                string description = Text(entries[folder + "avatar-pack.lua"]);
                StringAssert.DoesNotContain("__avatar-pack-" + id + "__", description);
                StringAssert.Contains("\"__avatar-bundle-my-server__/characters/" + id + "/graphics/icon.png\"", description);
                Assert.That(entries.ContainsKey(folder + "graphics/icon.png"), Is.True);
                Assert.That(entries.ContainsKey(folder + "graphics/idle-base.png"), Is.True);
                Assert.That(entries.ContainsKey(folder + "README.txt"), Is.True);
                Assert.That(entries.ContainsKey(folder + "pack.json"), Is.True);
                // the pack's own mod files stay behind
                Assert.That(entries.ContainsKey(folder + "info.json"), Is.False);
                Assert.That(entries.ContainsKey(folder + "thumbnail.png"), Is.False);
                Assert.That(entries.Keys.Any(k => k.StartsWith(folder + "locale/", StringComparison.Ordinal)), Is.False);
            }
            Assert.That(entries.ContainsKey(root + "thumbnail.png"), Is.True);
            Image8 thumbnail = PngReader.Decode(entries[root + "thumbnail.png"]);
            Assert.That((thumbnail.Width, thumbnail.Height), Is.EqualTo((144, 144)));
            string readme = Text(entries[root + "README.txt"]);
            StringAssert.Contains("Draco (draco 2.0.0) by Tester", readme);
            StringAssert.Contains("/avatar-revert-all <id>", readme);
            StringAssert.Contains("avatar-bundle-my-server_1.0.3.zip", readme);
            StringAssert.Contains("avatar-bundle-my-server=Avatar bundle: My Server", Text(entries[root + "locale/en/locale.cfg"]));
        }

        [Test]
        public void IsDeterministic()
        {
            List<BundleCharacter> characters = PackBundler.Merge(new[] { Read(PackZip("mika", "Mika", "1.0.0"), "a") }, null);
            Assert.That(Bundle(Info(), characters), Is.EqualTo(Bundle(Info(), characters)));
        }

        [Test]
        public void ABundleCanBeCombinedAgain()
        {
            List<BundleCharacter> first = PackBundler.Merge(new[]
            {
                Read(PackZip("mika", "Mika", "1.0.0"), "mika.zip"),
                Read(PackZip("draco", "Draco", "2.0.0"), "draco.zip"),
            }, null);
            BundleInput bundle = Read(Bundle(Info("1.0.0"), first), "old-bundle.zip");
            Assert.That(bundle.IsBundle, Is.True);
            Assert.That(bundle.Version, Is.EqualTo("1.0.0"));
            Assert.That(bundle.Characters.Select(c => (c.Id, c.Title, c.Author, c.Version)), Is.EqualTo(new[]
            {
                ("draco", "Draco", "Tester", "2.0.0"), ("mika", "Mika", "Tester", "1.0.0"),
            }));

            // a new player's pack joins; the bundle moves to another name: every path follows
            var notes = new List<string>();
            List<BundleCharacter> second = PackBundler.Merge(new[] { bundle, Read(PackZip("ash", "Ash", "1.0.0"), "ash.zip") }, notes);
            var info = new BundleInfo { Id = "other", Title = "Other", Version = "1.0.1", GeneratorVersion = "0.4.0" };
            Dictionary<string, byte[]> entries = Entries(Bundle(info, second));
            const string root = "avatar-bundle-other_1.0.1/";
            Assert.That(Text(entries[root + "data.lua"]), Is.EqualTo(PackBundler.DataLua("other", new[] { "ash", "draco", "mika" })));
            foreach (string id in new[] { "ash", "draco", "mika" })
            {
                string description = Text(entries[root + "characters/" + id + "/avatar-pack.lua"]);
                StringAssert.DoesNotContain("avatar-bundle-my-server", description);
                StringAssert.DoesNotContain("__avatar-pack-", description);
                StringAssert.Contains("\"__avatar-bundle-other__/characters/" + id + "/graphics/", description);
            }
            Assert.That(notes, Is.Empty);
            Assert.That(entries.ContainsKey(root + "characters/draco/pack.json"), Is.True);
            StringAssert.Contains("\"version\": \"2.0.0\"", Text(entries[root + "characters/draco/pack.json"]));
        }

        [Test]
        public void TheNewestVersionOfACharacterWins()
        {
            var notes = new List<string>();
            List<BundleCharacter> merged = PackBundler.Merge(new[]
            {
                Read(PackZip("mika", "Mika new", "1.1.0"), "new.zip"),
                Read(PackZip("mika", "Mika old", "1.0.9"), "old.zip"),
                Read(PackZip("draco", "Draco", "1.0.0"), "draco-a.zip"),
                Read(PackZip("draco", "Draco again", "1.0.0"), "draco-b.zip"),
            }, notes);
            Assert.That(merged.Select(c => (c.Id, c.Title)), Is.EqualTo(new[] { ("draco", "Draco again"), ("mika", "Mika new") }));
            Assert.That(notes, Has.Count.EqualTo(2));
            Assert.That(notes.Any(n => n.Contains("kept 1.1.0 from new.zip, not the older 1.0.9 from old.zip")), Is.True, string.Join("\n", notes));
            Assert.That(notes.Any(n => n.Contains("draco-b.zip replaces the same version from draco-a.zip")), Is.True, string.Join("\n", notes));
        }

        [Test]
        public void RefusesWhatIsNotAPackOrBundle()
        {
            using (var stream = new MemoryStream())
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    using (var writer = new StreamWriter(archive.CreateEntry("some-mod_1.0.0/info.json").Open()))
                    {
                        writer.Write("{\"name\": \"some-mod\", \"version\": \"1.0.0\"}");
                    }
                }
                var e = Assert.Throws<InvalidDataException>(() => Read(stream.ToArray(), "some-mod.zip"));
                StringAssert.Contains("\"some-mod\", not an avatar pack", e.Message);
            }
            using (var stream = new MemoryStream())
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    archive.CreateEntry("a/info.json");
                    archive.CreateEntry("b/info.json");
                }
                var e = Assert.Throws<InvalidDataException>(() => Read(stream.ToArray(), "two.zip"));
                StringAssert.Contains("exactly one folder", e.Message);
            }
            Assert.Throws<InvalidDataException>(() => Read(Encoding.UTF8.GetBytes("not a zip"), "x.zip"));
        }

        [Test]
        public void RefusesABadBundle()
        {
            List<BundleCharacter> one = PackBundler.Merge(new[] { Read(PackZip("mika", "Mika", "1.0.0"), "a") }, null);
            Assert.Throws<ArgumentException>(() => Bundle(new BundleInfo { Id = "Bad Id", Title = "x" }, one));
            Assert.Throws<ArgumentException>(() => Bundle(new BundleInfo { Id = "ok", Title = "x", Version = "1.0" }, one));
            Assert.Throws<ArgumentException>(() => Bundle(new BundleInfo { Id = "ok", Title = "" }, one));
            Assert.Throws<ArgumentException>(() => Bundle(new BundleInfo { Id = "ok", Title = "x" }, new List<BundleCharacter>()));
            Assert.Throws<ArgumentException>(() => Bundle(new BundleInfo { Id = "ok", Title = "x" }, new[] { one[0], one[0] }));
        }

        [Test]
        public void ComparesAndBumpsVersions()
        {
            Assert.That(PackBundler.CompareVersions("1.0.10", "1.0.9"), Is.GreaterThan(0));
            Assert.That(PackBundler.CompareVersions("1.2.0", "1.10.0"), Is.LessThan(0));
            Assert.That(PackBundler.CompareVersions("2.0.0", "2.0.0"), Is.EqualTo(0));
            Assert.That(PackBundler.CompareVersions("x", "0.0.0"), Is.LessThan(0));
            Assert.That(PackBundler.NextVersion("1.0.9"), Is.EqualTo("1.0.10"));
            Assert.That(PackBundler.NextVersion("nonsense"), Is.EqualTo("1.0.0"));
        }

        [Test]
        public void DataLuaMatchesPythonSpec()
        {
            string[] ids = { "mika", "draco", "a-1" };
            string output = PythonTools.Run(@"
from avatar_pack import spec
sys.stdout.write(spec.bundle_data_lua('my-server', ['mika', 'draco', 'a-1']))
");
            Assert.That(output, Is.EqualTo(PackBundler.DataLua("my-server", ids)));
        }

        [Test]
        public void ThePythonCheckerAcceptsABundle()
        {
            List<BundleCharacter> characters = PackBundler.Merge(new[]
            {
                Read(PackZip("mika", "Mika", "1.0.0", flight: true), "mika.zip"),
                Read(PackZip("draco", "Draco", "2.0.0"), "draco.zip"),
            }, null);
            string folder = Path.Combine(Path.GetTempPath(), "bundle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string zip = Path.Combine(folder, PackBundler.ZipFileName("my-server", "1.0.0"));
                File.WriteAllBytes(zip, Bundle(Info(), characters));
                (int exitCode, string output) = PythonTools.RunTool("tools/validate_sprites.py", "--pack", zip);
                Assert.That(exitCode, Is.EqualTo(0), output);
                StringAssert.Contains("OK: avatar-bundle-my-server_1.0.0.zip", output);
                Assert.That(output, Does.Not.Contain("warning"), output);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}

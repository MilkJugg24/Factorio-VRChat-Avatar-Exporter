using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace MilkJugg24.FactorioAvatarExporter.Core.Tests.Pack
{
    public class PackWriterTests
    {
        private static MemoryPackOutput Write(PackContent content)
        {
            var output = new MemoryPackOutput();
            new PackWriter(TestPng.Encode).Write(content, output);
            return output;
        }

        [Test]
        public void WritesEveryFileInOrdinalOrder()
        {
            PackContent content = SyntheticPack.Build(true, true);
            MemoryPackOutput output = Write(content);
            Assert.That(output.PackId, Is.EqualTo("test-avatar"));
            Assert.That(output.Version, Is.EqualTo("1.0.0"));
            Assert.That(output.Completed);
            Assert.That(output.Paths, Is.EqualTo(output.Paths.OrderBy(p => p, StringComparer.Ordinal).ToList()));

            var expected = new List<string>
            {
                "README.txt", "data.lua", "graphics/icon.png", "info.json", "locale/en/locale.cfg",
                "prototypes/avatar-pack.lua", "thumbnail.png",
            };
            foreach (KeyValuePair<string, Dictionary<string, SheetSet>> animation in content.Animations)
            {
                foreach (KeyValuePair<string, SheetSet> layer in animation.Value)
                {
                    int count = layer.Value.Files.Count;
                    expected.AddRange(Enumerable.Range(0, count).Select(k => PackFiles.Sheet(animation.Key, layer.Key, k, count)));
                }
            }
            Assert.That(output.Paths, Is.EquivalentTo(expected));
            Assert.That(output.Paths, Does.Contain("graphics/run-gun-base-1.png").And.Contain("graphics/run-gun-base-2.png"));
            Assert.That(output.Paths, Does.Contain("graphics/run-gun-shadow.png").And.Contain("graphics/run-gun-shadow-flipped.png"));
        }

        [Test]
        public void PngFilesComeFromTheEncoder()
        {
            PackContent content = SyntheticPack.Build(false, false);
            var encoded = new Dictionary<Image8, byte[]>();
            var output = new MemoryPackOutput();
            new PackWriter(image =>
            {
                byte[] png = TestPng.Encode(image);
                encoded.Add(image, png);
                return png;
            }).Write(content, output);
            Assert.That(output.Files[PackFiles.Icon], Is.SameAs(encoded[content.Icon]));
            Assert.That(output.Files[PackFiles.Thumbnail], Is.SameAs(encoded[content.Thumbnail]));
            Assert.That(output.Files["graphics/idle-base.png"], Is.SameAs(encoded[content.Animations["idle"][PackLayers.Base].Files[0]]));
            Assert.That(output.Files.Keys.Count(k => k.EndsWith(".png", StringComparison.Ordinal)), Is.EqualTo(encoded.Count));
        }

        [Test]
        public void TextFilesAreUtf8WithoutBomAndUnixLineEnds()
        {
            PackContent content = SyntheticPack.Build(false, false);
            content.Title = "ミカ 🐉";
            content.Permission = "Line one\r\nLine two\rLine three";
            MemoryPackOutput output = Write(content);
            foreach (string path in new[] { PackFiles.InfoJson, PackFiles.DataLua, PackFiles.Description, PackFiles.Locale, PackFiles.Readme })
            {
                byte[] data = output.Files[path];
                Assert.That(data.Take(3), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }), path);
                string text = output.Text(path); // throws on invalid UTF-8
                Assert.That(text, Does.Not.Contain("\r"), path);
                Assert.That(text, Does.EndWith("\n"), path);
            }
            StringAssert.Contains("ミカ 🐉", output.Text(PackFiles.Readme));
            StringAssert.Contains("Line one\nLine two\nLine three\n", output.Text(PackFiles.Readme));
        }

        [Test]
        public void DataLuaIsTheStandardOne()
        {
            MemoryPackOutput output = Write(SyntheticPack.Build(false, false));
            Assert.That(output.Text(PackFiles.DataLua), Is.EqualTo(PackFormat.DataLua));
            string python = PythonTools.Run("from avatar_pack import spec\nsys.stdout.write(spec.DATA_LUA)");
            Assert.That(output.Files[PackFiles.DataLua], Is.EqualTo(Encoding.UTF8.GetBytes(python)));
        }

        [Test]
        public void InfoJsonDescribesTheMod()
        {
            PackContent content = SyntheticPack.Build(false, false);
            content.Title = "A \"quoted\" \\ title with ミ " + new string('x', 50); // 77 characters, 104 bytes in info.json
            content.Version = "2.3.4";
            using (JsonDocument doc = JsonDocument.Parse(Write(content).Files[PackFiles.InfoJson]))
            {
                JsonElement info = doc.RootElement;
                Assert.That(info.EnumerateObject().Select(p => p.Name), Is.EqualTo(new[]
                {
                    "name", "version", "title", "author", "factorio_version", "description", "dependencies",
                }));
                Assert.That(info.GetProperty("name").GetString(), Is.EqualTo("avatar-pack-test-avatar"));
                Assert.That(info.GetProperty("version").GetString(), Is.EqualTo("2.3.4"));
                Assert.That(info.GetProperty("title").GetString(), Is.EqualTo(PackNaming.InfoTitle(content.Title, content.Author)));
                Assert.That(Encoding.UTF8.GetByteCount(info.GetProperty("title").GetString()), Is.LessThanOrEqualTo(100));
                Assert.That(info.GetProperty("author").GetString(), Is.EqualTo("Tester"));
                Assert.That(info.GetProperty("factorio_version").GetString(), Is.EqualTo("2.1"));
                StringAssert.Contains("Test Model", info.GetProperty("description").GetString());
                Assert.That(info.GetProperty("dependencies").EnumerateArray().Select(d => d.GetString()),
                    Is.EqualTo(new[] { "avatar-characters >= 0.5.0" }));
            }
            // Factorio refuses a mod without an author ("Key "author" not found in property tree")
            content.Author = null;
            using (JsonDocument doc = JsonDocument.Parse(Write(content).Files[PackFiles.InfoJson]))
            {
                Assert.That(doc.RootElement.GetProperty("author").GetString(), Is.EqualTo("Anonymous"));
            }
        }

        [Test]
        public void InfoJsonEscapesEveryCharacter()
        {
            // the validator keeps control characters out of packs, but the JSON stays valid regardless
            PackContent content = SyntheticPack.Build(false, false);
            content.Author = "q\" b\\ n\n r\r t\t c\u0001 d\u007f ü 🐉";
            string json = PackDocuments.InfoJson(content);
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                Assert.That(doc.RootElement.GetProperty("author").GetString(), Is.EqualTo(content.Author));
            }
            StringAssert.Contains("\\u0001", json);
            StringAssert.Contains("ü 🐉", json);
        }

        [Test]
        public void DescriptionReadsBackWithTheLuaTableTool()
        {
            PackContent content = SyntheticPack.Build(true, true);
            content.AnimationSpeeds["idle"] = 0.08;
            content.AnimationSpeeds["run-gun"] = 0.5;
            content.Timing.DistancePerFrame = 0.15;
            string text = Write(content).Text(PackFiles.Description);
            Assert.That(text, Does.StartWith("-- Written by factorio-avatar-exporter 0.1.0.\nreturn {\n"));
            const string script = @"
import json
import lua_table
print(json.dumps(lua_table.loads(sys.stdin.read())))
";
            using (JsonDocument doc = JsonDocument.Parse(PythonTools.Run(script, text)))
            {
                JsonElement d = doc.RootElement;
                Assert.That(d.GetProperty("format_version").GetInt32(), Is.EqualTo(1));
                Assert.That(d.GetProperty("id").GetString(), Is.EqualTo("test-avatar"));
                Assert.That(d.GetProperty("title").GetString(), Is.EqualTo("Test Avatar"));
                Assert.That(d.GetProperty("author").GetString(), Is.EqualTo("Tester"));
                Assert.That(d.GetProperty("generator").GetProperty("name").GetString(), Is.EqualTo("factorio-avatar-exporter"));
                Assert.That(d.GetProperty("generator").GetProperty("version").GetString(), Is.EqualTo("0.1.0"));
                Assert.That(d.GetProperty("icon").GetProperty("filename").GetString(), Is.EqualTo("__avatar-pack-test-avatar__/graphics/icon.png"));
                Assert.That(d.GetProperty("icon").GetProperty("size").GetInt32(), Is.EqualTo(64));
                Assert.That(d.GetProperty("pixels_per_tile").GetInt32(), Is.EqualTo(64));
                Assert.That(d.GetProperty("tintable").GetBoolean(), Is.True);
                Assert.That(d.GetProperty("flight").GetBoolean(), Is.True);

                JsonElement timing = d.GetProperty("timing");
                Assert.That(timing.GetProperty("distance_per_frame").GetDouble(), Is.EqualTo(0.15));
                Assert.That(timing.GetProperty("mine_impact_frame").GetInt32(), Is.EqualTo(19));
                Assert.That(timing.GetProperty("left_foot_frames").EnumerateArray().Select(f => f.GetInt32()), Is.EqualTo(new[] { 1 }));
                Assert.That(timing.GetProperty("right_foot_frames").EnumerateArray().Select(f => f.GetInt32()), Is.EqualTo(new[] { 7 }));
                Assert.That(timing.GetProperty("animation_speed").GetProperty("idle").GetDouble(), Is.EqualTo(0.08));
                Assert.That(timing.GetProperty("animation_speed").GetProperty("run-gun").GetDouble(), Is.EqualTo(0.5));

                JsonElement animations = d.GetProperty("animations");
                Assert.That(animations.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(AnimationSpec.All.Select(a => a.Name)));
                foreach (KeyValuePair<string, Dictionary<string, SheetSet>> animation in content.Animations)
                {
                    JsonElement a = animations.GetProperty(animation.Key);
                    AnimationInfo info = AnimationSpec.Get(animation.Key);
                    Assert.That(a.GetProperty("direction_count").GetInt32(), Is.EqualTo(info.DirectionCount));
                    Assert.That(a.GetProperty("frame_count").GetInt32(), Is.EqualTo(info.FrameCount));
                    foreach (KeyValuePair<string, SheetSet> layer in animation.Value)
                    {
                        JsonElement l = a.GetProperty("layers").GetProperty(layer.Key);
                        SheetSet s = layer.Value;
                        Assert.That(l.GetProperty("width").GetInt32(), Is.EqualTo(s.FrameWidth));
                        Assert.That(l.GetProperty("height").GetInt32(), Is.EqualTo(s.FrameHeight));
                        Assert.That(l.GetProperty("shift")[0].GetDouble(), Is.EqualTo(s.ShiftX));
                        Assert.That(l.GetProperty("shift")[1].GetDouble(), Is.EqualTo(s.ShiftY));
                        string prefix = "__avatar-pack-test-avatar__/graphics/" + animation.Key + "-" + layer.Key;
                        if (s.Files.Count > 1)
                        {
                            Assert.That(l.TryGetProperty("filename", out _), Is.False);
                            Assert.That(l.TryGetProperty("lines_per_file", out JsonElement lines)
                                ? lines.GetInt32() : (int?)null, Is.EqualTo(s.LinesPerFile));
                            Assert.That(l.TryGetProperty("frames_per_file", out JsonElement perFile)
                                ? perFile.GetInt32() : (int?)null, Is.EqualTo(s.FramesPerFile));
                            Assert.That(l.GetProperty("filenames").EnumerateArray().Select(f => f.GetString()),
                                Is.EqualTo(Enumerable.Range(1, s.Files.Count).Select(n => prefix + "-" + n + ".png")));
                        }
                        else
                        {
                            Assert.That(l.TryGetProperty("filenames", out _), Is.False);
                            Assert.That(l.GetProperty("filename").GetString(), Is.EqualTo(prefix + ".png"));
                        }
                    }
                }
            }
        }

        [Test]
        public void DescriptionLeavesOutOptionalFields()
        {
            PackContent content = SyntheticPack.Build(false, false, 32);
            content.Author = "";
            Dictionary<string, object> table = PackDescription.Table(content);
            Assert.That(table.ContainsKey("author"), Is.False);
            Assert.That(table["tintable"], Is.EqualTo(false));
            Assert.That(table["flight"], Is.EqualTo(false));
            Assert.That(table["pixels_per_tile"], Is.EqualTo(32));
            Assert.That(((Dictionary<string, object>)table["timing"]).ContainsKey("animation_speed"), Is.False);
            Assert.That(((Dictionary<string, object>)table["animations"]).Keys, Is.EquivalentTo(AnimationSpec.ForPack(false).Select(a => a.Name)));
        }

        [Test]
        public void LocaleAndReadme()
        {
            PackContent content = SyntheticPack.Build(false, false);
            content.Permission = "  CC BY 4.0 by Someone.\nAttribution: see their page.  ";
            MemoryPackOutput output = Write(content);
            string locale = output.Text(PackFiles.Locale);
            Assert.That(locale, Is.EqualTo(
                "[mod-name]\navatar-pack-test-avatar=VRC Avatar: Test Avatar by Tester\n\n" +
                "[mod-description]\navatar-pack-test-avatar=" + PackDocuments.ModDescription(content) + "\n"));
            string readme = output.Text(PackFiles.Readme);
            Assert.That(readme, Does.StartWith("Test Avatar\n"));
            StringAssert.Contains("Author:         Tester\n", readme);
            StringAssert.Contains("Source avatar:  Test Model\n", readme);
            StringAssert.Contains("Made with:      factorio-avatar-exporter 0.1.0\n", readme);
            StringAssert.Contains("Permission\n----------\nCC BY 4.0 by Someone.\nAttribution: see their page.\n", readme);
            StringAssert.Contains("avatar-pack-test-avatar_1.0.0.zip", readme);
            StringAssert.Contains("\n    /avatar-revert-all test-avatar\n", readme);
            content.Author = null;
            content.Permission = null;
            Assert.That(PackDocuments.Readme(content), Does.Not.Contain("Author:"));
            Assert.That(PackDocuments.Readme(content), Does.Not.Contain("Permission"));
        }

        [Test]
        public void RefusesInvalidPacksBeforeTouchingTheOutput()
        {
            PackContent content = SyntheticPack.Build(false, false);
            content.Id = "Bad Id";
            content.Animations.Remove("idle");
            var output = new MemoryPackOutput();
            var e = Assert.Throws<InvalidOperationException>(() => new PackWriter(TestPng.Encode).Write(content, output));
            StringAssert.Contains("the required \"idle\" animation is missing", e.Message);
            Assert.That(output.PackId, Is.Null);
            Assert.That(output.Paths, Is.Empty);
        }

        [Test]
        public void EncoderFailuresLeaveTheOutputUntouched()
        {
            var output = new MemoryPackOutput();
            Assert.Throws<InvalidOperationException>(() => new PackWriter(_ => null).Write(SyntheticPack.Build(false, false), output));
            Assert.Throws<InvalidOperationException>(() => new PackWriter(_ => new byte[0]).Write(SyntheticPack.Build(false, false), output));
            Assert.That(output.PackId, Is.Null);
            Assert.That(output.Paths, Is.Empty);
        }

        [Test]
        public void ChecksArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new PackWriter(null));
            var writer = new PackWriter(TestPng.Encode);
            Assert.Throws<ArgumentNullException>(() => writer.Write(null, new MemoryPackOutput()));
            Assert.Throws<ArgumentNullException>(() => writer.Write(SyntheticPack.Build(false, false), null));
        }
    }
}

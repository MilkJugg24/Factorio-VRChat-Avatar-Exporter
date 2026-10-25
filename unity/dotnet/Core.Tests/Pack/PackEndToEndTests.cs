using System.IO;
using System.Linq;
using NUnit.Framework;

namespace FactorioAvatarExporter.Core.Tests.Pack
{
    /// <summary>Writes whole packs and checks them with tools/validate_sprites.py --pack.</summary>
    public class PackEndToEndTests
    {
        private static void WriteAndValidate(PackContent content)
        {
            PythonTools.RequirePython();
            var writer = new PackWriter(TestPng.Encode);
            using (var temp = new TempDirectory())
            {
                var folder = new DirectoryPackOutput(temp.Path, false);
                writer.Write(content, folder);
                string zipPath = Path.Combine(temp.Path, PackNaming.ZipFileName(content.Id, content.Version));
                using (FileStream stream = File.Create(zipPath))
                {
                    writer.Write(content, new ZipPackOutput(stream));
                }

                (int exitCode, string output) = PythonTools.RunTool("tools/validate_sprites.py", "--pack", folder.PackDirectory, zipPath);
                Assert.That(exitCode, Is.EqualTo(0), output);
                string[] lines = output.Split('\n').Where(l => l.Length > 0).ToArray();
                Assert.That(lines.Length, Is.EqualTo(2), output);
                Assert.That(lines[0], Does.StartWith("OK: " + PackNaming.ModName(content.Id) + ": "));
                Assert.That(lines[1], Does.StartWith("OK: " + PackNaming.ZipFileName(content.Id, content.Version) + ": "));
                int sheets = content.Animations.Values.SelectMany(layers => layers.Values).Sum(s => s.Files.Count);
                Assert.That(lines[0], Does.Contain(": " + sheets + " sprite sheets"), "every sheet was checked");
            }
        }

        [Test]
        public void FullPackWithFlightMasksAndSplitSheetsPassesTheValidator()
        {
            PackContent content = SyntheticPack.Build(true, true);
            Assert.That(content.Animations["run-gun"][PackLayers.Base].LinesPerFile, Is.Not.Null, "some sheets are split");
            WriteAndValidate(content);
        }

        [Test]
        public void GroundOnlyLowResolutionPackPassesTheValidator()
        {
            PackContent content = SyntheticPack.Build(false, false, 32, "low-res-2");
            content.Author = null;
            content.Title = "ミカ (low res) 🐉";
            content.Version = "0.3.10";
            content.AnimationSpeeds["idle"] = 0.08;
            content.AnimationSpeeds["corpse"] = 1.0;
            content.Icon = SyntheticPack.Filled(32, 32, 1, 2, 3, 255);
            WriteAndValidate(content);
        }
    }
}

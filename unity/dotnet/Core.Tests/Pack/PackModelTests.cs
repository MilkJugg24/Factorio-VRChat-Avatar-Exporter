using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MilkJugg24.FactorioAvatarExporter.Core.Tests.Pack
{
    public class SheetSetTests
    {
        private static Image8[] Files(params (int w, int h)[] sizes) => sizes.Select(s => new Image8(s.w, s.h)).ToArray();

        [Test]
        public void HoldsASplitSheet()
        {
            Image8[] files = Files((6, 9), (6, 9), (6, 6));
            var s = new SheetSet(8, 2, 3, 3, 0.5, -1.25, files, 3);
            Assert.That((s.DirectionCount, s.FrameCount, s.FrameWidth, s.FrameHeight), Is.EqualTo((8, 2, 3, 3)));
            Assert.That((s.ShiftX, s.ShiftY), Is.EqualTo((0.5, -1.25)));
            Assert.That(s.LinesPerFile, Is.EqualTo(3));
            Assert.That(Enumerable.Range(0, 3).Select(s.RowsInFile), Is.EqualTo(new[] { 3, 3, 2 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.RowsInFile(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.RowsInFile(-1));
            files[0] = null;
            Assert.That(s.Files[0], Is.Not.Null, "the list is copied");
            Assert.That(s.Files, Is.Not.InstanceOf<Image8[]>());
        }

        [Test]
        public void HoldsASheetSplitByFrames()
        {
            var s = new SheetSet(2, 5, 3, 4, 0, 0, Files((6, 8), (6, 8), (3, 8)), null, 2);
            Assert.That(s.FramesPerFile, Is.EqualTo(2));
            Assert.That(s.LinesPerFile, Is.Null);
            Assert.That(Enumerable.Range(0, 3).Select(s.FramesInFile), Is.EqualTo(new[] { 2, 2, 1 }));
            Assert.That(Enumerable.Range(0, 3).Select(s.FirstFrameInFile), Is.EqualTo(new[] { 0, 2, 4 }));
            Assert.That(Enumerable.Range(0, 3).Select(s.RowsInFile), Is.EqualTo(new[] { 2, 2, 2 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.FramesInFile(3));
            Assert.Throws<ArgumentException>(() => new SheetSet(2, 5, 3, 4, 0, 0, Files((6, 8), (6, 8)), null, 2), "needs three files");
            Assert.Throws<ArgumentException>(() => new SheetSet(2, 5, 3, 4, 0, 0, Files((6, 8), (6, 8), (6, 8)), null, 2), "the last file is narrower");
            Assert.Throws<ArgumentOutOfRangeException>(() => new SheetSet(2, 5, 3, 4, 0, 0, Files((15, 8)), null, 5));
            Assert.Throws<ArgumentException>(() => new SheetSet(4, 4, 1, 1, 0, 0, Files((2, 2), (2, 2), (2, 2), (2, 2)), 2, 2), "both ways");
        }

        [Test]
        public void RefusesInconsistentFiles()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SheetSet(0, 1, 1, 1, 0, 0, Files((1, 1)), null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SheetSet(1, 0, 1, 1, 0, 0, Files((1, 1)), null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SheetSet(1, 1, 0, 1, 0, 0, Files((1, 1)), null));
            Assert.Throws<ArgumentException>(() => new SheetSet(1, 1, 1, 1, double.NaN, 0, Files((1, 1)), null));
            Assert.Throws<ArgumentException>(() => new SheetSet(1, 1, 1, 1, 0, double.PositiveInfinity, Files((1, 1)), null));
            Assert.Throws<ArgumentException>(() => new SheetSet(1, 1, 1, 1, 0, 0, null, null));
            Assert.Throws<ArgumentException>(() => new SheetSet(1, 1, 1, 1, 0, 0, Files(), null));
            Assert.Throws<ArgumentException>(() => new SheetSet(1, 1, 1, 1, 0, 0, new Image8[] { null }, null));
            Assert.Throws<ArgumentException>(() => new SheetSet(2, 3, 4, 5, 0, 0, Files((12, 9)), null), "wrong height");
            Assert.Throws<ArgumentException>(() => new SheetSet(2, 3, 4, 5, 0, 0, Files((12, 10), (12, 10)), null), "one file without lines per file");
            Assert.Throws<ArgumentOutOfRangeException>(() => new SheetSet(2, 1, 1, 1, 0, 0, Files((1, 2)), 2), "lines per file must split");
            Assert.Throws<ArgumentOutOfRangeException>(() => new SheetSet(2, 1, 1, 1, 0, 0, Files((1, 1), (1, 1)), 0));
            Assert.Throws<ArgumentException>(() => new SheetSet(5, 1, 1, 1, 0, 0, Files((1, 2), (1, 2)), 2), "needs three files");
            Assert.Throws<ArgumentException>(() => new SheetSet(5, 1, 1, 1, 0, 0, Files((1, 2), (1, 2), (1, 2)), 2), "the last file is shorter");
            Assert.That(new SheetSet(5, 1, 1, 1, 0, 0, Files((1, 2), (1, 2), (1, 1)), 2).Files.Count, Is.EqualTo(3));
        }
    }

    public class PackContentTests
    {
        private static SheetSet OneFrame() => new SheetSet(1, 1, 1, 1, 0, 0, new[] { new Image8(1, 1) }, null);

        [Test]
        public void HasTheFormatDefaults()
        {
            var content = new PackContent();
            Assert.That(content.Version, Is.EqualTo("1.0.0"));
            Assert.That(content.GeneratorName, Is.EqualTo("factorio-avatar-exporter"));
            Assert.That(content.PixelsPerTile, Is.EqualTo(64));
            Assert.That(content.Flight, Is.False);
            Assert.That(content.Timing.MineImpactFrame, Is.EqualTo(19));
            Assert.That(content.AnimationSpeeds, Is.Empty);
            Assert.That(content.Tintable, Is.False);
        }

        [Test]
        public void SetLayerAddsAnimationsAndTintableFollowsMasks()
        {
            var content = new PackContent();
            content.SetLayer("idle", PackLayers.Base, OneFrame());
            content.SetLayer("idle", PackLayers.Shadow, OneFrame());
            Assert.That(content.Animations["idle"].Keys, Is.EquivalentTo(new[] { "base", "shadow" }));
            Assert.That(content.Tintable, Is.False);
            content.SetLayer("run", PackLayers.Mask, OneFrame());
            Assert.That(content.Tintable, Is.True);
            content.Animations["broken"] = null;
            content.SetLayer("broken", PackLayers.Base, OneFrame());
            Assert.That(content.Animations["broken"].Count, Is.EqualTo(1));
            Assert.Throws<ArgumentNullException>(() => content.SetLayer(null, PackLayers.Base, OneFrame()));
            Assert.Throws<ArgumentNullException>(() => content.SetLayer("idle", null, OneFrame()));
            Assert.Throws<ArgumentNullException>(() => content.SetLayer("idle", PackLayers.Base, null));
        }

        [Test]
        public void OptionalLayers()
        {
            Assert.That(PackLayers.OptionalFor("run-gun"), Is.EqualTo(new[] { "shadow-flipped" }));
            Assert.That(PackLayers.OptionalFor("fly-gun"), Is.Empty);
            Assert.That(PackLayers.OptionalFor("idle"), Is.Empty);
        }

        [Test]
        public void DocumentBuildersRefuseNull()
        {
            Assert.Throws<ArgumentNullException>(() => PackDescription.Table(null));
            Assert.Throws<ArgumentNullException>(() => PackDescription.FileText(null));
            Assert.Throws<ArgumentNullException>(() => PackDocuments.InfoJson(null));
            Assert.Throws<ArgumentNullException>(() => PackDocuments.Locale(null));
            Assert.Throws<ArgumentNullException>(() => PackDocuments.Readme(null));
            Assert.Throws<ArgumentNullException>(() => PackDocuments.ModDescription(null));
        }
    }

    public class PackFilesTests
    {
        [Test]
        public void SheetFileNames()
        {
            Assert.That(PackFiles.Sheet("idle", "base", 0, 1), Is.EqualTo("graphics/idle-base.png"));
            Assert.That(PackFiles.Sheet("run-gun", "shadow-flipped", 0, 1), Is.EqualTo("graphics/run-gun-shadow-flipped.png"));
            Assert.That(PackFiles.Sheet("run-gun", "base", 0, 3), Is.EqualTo("graphics/run-gun-base-1.png"));
            Assert.That(PackFiles.Sheet("run-gun", "base", 2, 3), Is.EqualTo("graphics/run-gun-base-3.png"));
            Assert.Throws<ArgumentOutOfRangeException>(() => PackFiles.Sheet("idle", "base", 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => PackFiles.Sheet("idle", "base", 0, 0));
        }

        [Test]
        public void ModPaths()
        {
            Assert.That(PackFiles.ModPath("mika", PackFiles.Icon), Is.EqualTo("__avatar-pack-mika__/graphics/icon.png"));
            Assert.Throws<ArgumentException>(() => PackFiles.ModPath("Mika", PackFiles.Icon));
        }

        [Test]
        public void RelativePaths()
        {
            var good = new List<string>
            {
                PackFiles.InfoJson, PackFiles.DataLua, PackFiles.Description, PackFiles.Locale, PackFiles.Readme,
                PackFiles.Thumbnail, PackFiles.Icon, "graphics/run-gun-base-2.png", "a/b/c.d", "..a", "a..",
            };
            foreach (string path in good)
            {
                Assert.DoesNotThrow(() => PackFiles.CheckRelativePath(path), path);
            }
            foreach (string path in new[] { null, "", "/a", "a/", "a//b", ".", "..", "a/../b", "a/./b", "a\\b", "c:", "a\tb" })
            {
                Assert.Throws<ArgumentException>(() => PackFiles.CheckRelativePath(path), path ?? "null");
            }
        }
    }
}

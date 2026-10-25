using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MilkJugg24.FactorioAvatarExporter.Core.Tests.Pack
{
    public class PackValidatorTests
    {
        [Test]
        public void SyntheticPacksAreValid()
        {
            Assert.That(PackValidator.Validate(SyntheticPack.Build(true, true)), Is.Empty);
            Assert.That(PackValidator.Validate(SyntheticPack.Build(false, false, 32)), Is.Empty);
            PackContent noAuthor = SyntheticPack.Build(false, false);
            noAuthor.Author = null;
            noAuthor.AnimationSpeeds["idle"] = 0.08;
            noAuthor.Title = new string('ミ', PackFormat.MaxTitleLength);
            Assert.That(PackValidator.Validate(noAuthor), Is.Empty);
            Assert.Throws<ArgumentNullException>(() => PackValidator.Validate(null));
        }

        private static SheetSet Sheets(int directions, int frames, int width = 4, int height = 4) =>
            SheetBuilder.Build(SyntheticPack.Frames(directions, frames, Math.Max(width, 5), Math.Max(height, 5), 1), 2, 2, 64);

        public static IEnumerable<TestCaseData> Mistakes()
        {
            TestCaseData Case(string name, Action<PackContent> change, string error) =>
                new TestCaseData(change, error).SetName("Reports" + name);

            yield return Case("BadId", p => p.Id = "Mika", "the id \"Mika\" is not valid");
            yield return Case("CorpseId", p => p.Id = "corpse-mika", "the id \"corpse-mika\" is not valid");
            yield return Case("SprintId", p => p.Id = "sprint-mika", "the id \"sprint-mika\" is not valid");
            yield return Case("MissingId", p => p.Id = null, "the id \"\" is not valid");
            yield return Case("EmptyTitle", p => p.Title = " ", "the title is empty");
            yield return Case("LongTitle", p => p.Title = new string('x', 81), "the title has 81 characters; at most 80");
            yield return Case("MultilineTitle", p => p.Title = "a\nb", "the title must be a single line");
            yield return Case("MultilineAuthor", p => p.Author = "a\tb", "the author must be a single line");
            yield return Case("BadVersion", p => p.Version = "1.0", "the version \"1.0\" must look like 1.0.0");
            yield return Case("MissingSource", p => p.SourceAvatar = "", "the name of the source avatar is missing");
            yield return Case("MissingGenerator", p => p.GeneratorVersion = null, "the generator name and version are required");
            yield return Case("BadPixelsPerTile", p => p.PixelsPerTile = 48, "pixels per tile is 48; only 64 and 32");
            yield return Case("MissingIcon", p => p.Icon = null, "the icon is missing");
            yield return Case("NonSquareIcon", p => p.Icon = new Image8(64, 32), "the icon is 64x32 px; it must be square");
            yield return Case("HugeIcon", p => p.Icon = new Image8(513, 513), "at most 512x512");
            yield return Case("MissingThumbnail", p => p.Thumbnail = null, "the thumbnail is missing");
            yield return Case("WrongThumbnail", p => p.Thumbnail = new Image8(64, 64), "the thumbnail is 64x64 px; it must be 144x144");
            yield return Case("MissingAnimation", p => p.Animations.Remove("mine"), "the required \"mine\" animation is missing");
            yield return Case("PartialFlight", p => p.Animations.Remove("fly"), "Flight is on, but the \"fly\" animation is missing");
            yield return Case("FlightAnimationWithoutFlight", p =>
            {
                p.Flight = false;
                p.Animations.Remove("fly");
                p.Animations.Remove("hover");
                p.Animations.Remove("hover-gun");
                p.Animations.Remove("fly-gun");
            }, "the \"take-off\" animation is a flight animation, but Flight is off");
            yield return Case("UnknownAnimation", p => p.SetLayer("swim", PackLayers.Base, Sheets(8, 2)),
                "\"swim\" is not an animation of the pack format");
            yield return Case("NoLayers", p => p.Animations["idle"].Clear(), "animation \"idle\" has no layers");
            yield return Case("MissingBase", p => p.Animations["idle"].Remove(PackLayers.Base), "animation \"idle\" is missing its \"base\" layer");
            yield return Case("MissingShadow", p => p.Animations["corpse"].Remove(PackLayers.Shadow), "animation \"corpse\" is missing its \"shadow\" layer");
            yield return Case("MaskNotEverywhere", p => p.Animations["run"].Remove(PackLayers.Mask),
                "animation \"run\" is missing its \"mask\" layer (other animations have one");
            yield return Case("UnusedLayer", p => p.SetLayer("idle", PackLayers.ShadowFlipped, Sheets(8, 8)),
                "animation \"idle\" has a \"shadow-flipped\" layer, which the pack format does not use");
            yield return Case("WrongDirections", p => p.SetLayer("run", PackLayers.Shadow, Sheets(4, 12)),
                "animation \"run\" layer \"shadow\" has 4 directions; the pack format needs 8");
            yield return Case("FrameCountsDiffer", p => p.SetLayer("run", PackLayers.Shadow, Sheets(8, 7)),
                "animation \"run\" layer \"shadow\" has 7 frames, but layer \"base\" has 12");
            yield return Case("TooManyFrames", p =>
            {
                foreach (string layer in new[] { PackLayers.Base, PackLayers.Mask, PackLayers.Shadow })
                {
                    p.SetLayer("corpse", layer, new SheetSet(1, 256, 1, 1, 0, 0, new[] { new Image8(256, 1) }, null));
                }
            }, "animation \"corpse\" has 256 frames; at most 255");
            yield return Case("WideSheet", p => p.SetLayer("corpse", PackLayers.Shadow,
                new SheetSet(1, 1, 4097, 1, 0, 0, new[] { new Image8(4097, 1) }, null)),
                "animation \"corpse\" layer \"shadow\" file 1 is 4097x1 px; sheets may be at most 4096");
            yield return Case("TallSheet", p => p.SetLayer("run-gun", PackLayers.ShadowFlipped,
                new SheetSet(18, 12, 1, 300, 0, 0, new[] { new Image8(12, 300 * 14), new Image8(12, 300 * 4) }, 14)),
                "animation \"run-gun\" layer \"shadow-flipped\" file 1 is 12x4200 px");
            yield return Case("MissingTiming", p => p.Timing = null, "the timing is missing");
            yield return Case("BadDistance", p => p.Timing.DistancePerFrame = 0, "timing: the distance per frame must be above 0 and at most 2 tiles, not 0");
            yield return Case("BadSprintDistance", p => p.Timing.SprintDistancePerFrame = 3, "timing: the sprint's distance per frame must be above 0 and at most 2 tiles, not 3");
            yield return Case("NaNDistance", p => p.Timing.DistancePerFrame = double.NaN, "timing: the distance per frame must be above 0");
            yield return Case("BadImpactFrame", p => p.Timing.MineImpactFrame = 27,
                "timing: the mine impact frame 27 is not a frame of the mine animation (1 to 26)");
            yield return Case("BadFootFrame", p => p.Timing.RightFootFrames = new[] { 7, 0 },
                "timing: right foot frame 0 is not a frame of the run animation (1 to 12)");
            yield return Case("NoFootFrames", p => p.Timing.LeftFootFrames = new int[0], "timing: the left foot frames are missing");
            yield return Case("UnknownSpeed", p => p.AnimationSpeeds["swim"] = 1, "an animation speed is given for \"swim\"");
            yield return Case("BadSpeed", p => p.AnimationSpeeds["run"] = -0.5, "the animation speed of \"run\" must be a positive number, not -0.5");
            yield return Case("InfiniteSpeed", p => p.AnimationSpeeds["run"] = double.PositiveInfinity, "the animation speed of \"run\" must be a positive number");
        }

        [TestCaseSource(nameof(Mistakes))]
        public void ReportsMistakes(Action<PackContent> change, string error)
        {
            PackContent pack = SyntheticPack.Build(true, true);
            change(pack);
            IReadOnlyList<string> errors = PackValidator.Validate(pack);
            Assert.That(errors, Has.Some.Contains(error));
            Assert.That(errors.Count, Is.EqualTo(1), string.Join("\n", errors));
        }

        [Test]
        public void TimingIsCheckedAgainstThePacksOwnFrameCounts()
        {
            PackContent pack = SyntheticPack.Build(false, false);
            foreach (string layer in new[] { PackLayers.Base, PackLayers.Shadow })
            {
                pack.SetLayer("run", layer, Sheets(8, 6));
                pack.SetLayer("mine", layer, Sheets(8, 20));
            }
            pack.Timing.MineImpactFrame = 15;
            pack.Timing.RightFootFrames = new[] { 4 };
            Assert.That(PackValidator.Validate(pack), Is.EqualTo(new[] { "timing: the sprint has 12 frames and the run 6; they must be the same (the feet land on the same frames)" }));
            foreach (string layer in new[] { PackLayers.Base, PackLayers.Shadow })
            {
                pack.SetLayer("sprint", layer, Sheets(8, 6));
            }
            Assert.That(PackValidator.Validate(pack), Is.Empty);
            pack.Timing.RightFootFrames = new[] { 7 };
            Assert.That(PackValidator.Validate(pack), Is.EqualTo(new[] { "timing: right foot frame 7 is not a frame of the run animation (1 to 6)" }));
        }

        [Test]
        public void ReportsEveryProblemAtOnce()
        {
            PackContent pack = SyntheticPack.Build(false, false);
            pack.Id = "Bad Id";
            pack.Version = "1.0";
            pack.Animations.Remove("corpse");
            IReadOnlyList<string> errors = PackValidator.Validate(pack);
            Assert.That(errors.Count, Is.EqualTo(3), string.Join("\n", errors));
        }
    }
}

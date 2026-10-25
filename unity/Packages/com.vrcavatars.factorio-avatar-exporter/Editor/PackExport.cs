using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using FactorioAvatarExporter.Core;
using UnityEngine;

namespace FactorioAvatarExporter.Editor
{
    /// <summary>The version of this package, written into every pack (kept equal to package.json by a test).</summary>
    internal static class ExporterInfo
    {
        public const string Version = "0.24.0";
    }

    /// <summary>What the user chose for a pack.</summary>
    internal sealed class PackOptions
    {
        public string Title;
        public string Id;
        public string Author;
        public string Version = PackContent.DefaultVersion;
        /// <summary>
        /// "Normal Quality", the window's default: 32 pixels per tile, a quarter of the video memory of "High Quality"
        /// (64, <see cref="Conventions.DefaultPixelsPerTile"/>), the same size in the game.
        /// </summary>
        public const int DefaultPixelsPerTile = 32;

        public int PixelsPerTile = DefaultPixelsPerTile;
        public bool Flight = true;

        /// <summary>The file the pack's zip is saved as (the user chooses it; see <see cref="PackNaming.ZipFileName"/>).</summary>
        public string ZipPath;

        /// <summary>
        /// When something still reaches the edge of a frame on the retry, which measures what each frame draws, cut
        /// it off there instead of stopping; report.txt and the result list those frames.
        /// </summary>
        public bool AllowCutOff = true;

        /// <summary>Where report.txt and preview sheets go.</summary>
        public string PreviewFolder;

        /// <summary>What is wrong with these options, or null when the pack can be exported.</summary>
        public string Problem()
        {
            string title = (Title ?? "").Trim();
            int length = PackNaming.TitleLength(title);
            if (length == 0)
            {
                return "Enter a name: it's shown in the game's avatar window.";
            }
            if (length > 80 || title.IndexOfAny(new[] { '\n', '\r' }) >= 0)
            {
                return "The name must be at most 80 characters on one line.";
            }
            if (string.IsNullOrWhiteSpace(Id))
            {
                return "Enter a pack id (lowercase letters, digits and -).";
            }
            if (!PackNaming.IsValidId(Id))
            {
                return "The pack id must be lowercase letters, digits and -, at most 35 characters, not starting with corpse- or sprint-.";
            }
            if (!PackNaming.IsValidVersion(Version))
            {
                return "The version must look like 1.0.0.";
            }
            if (string.IsNullOrWhiteSpace(Author))
            {
                return "Enter an author (any name): Factorio needs one for every mod.";
            }
            return null;
        }
    }

    /// <summary>What an export wrote.</summary>
    internal sealed class PackExportResult
    {
        public string ZipPath;
        public string Report;

        /// <summary>Frames something reached the edge of and was cut off in (<see cref="PackOptions.AllowCutOff"/>).</summary>
        public IReadOnlyList<string> CutOff = new string[0];
    }

    /// <summary>Helpers for writing avatar packs from the Editor.</summary>
    internal static class PackExport
    {
        /// <summary>
        /// Renders every animation of <paramref name="poses"/> with <paramref name="renderer"/> and writes the
        /// pack's zip to <see cref="PackOptions.ZipPath"/>, then report.txt and a preview sheet per
        /// animation to <see cref="PackOptions.PreviewFolder"/>. Throws <see cref="OperationCanceledException"/>
        /// when cancelled (nothing is written then).
        /// </summary>
        public static PackExportResult Write(PackOptions options, string sourceAvatar, IPoseSource poses,
            StageRenderer renderer, IEnumerable<string> notes, Action<ExportProgress> progress, CancellationToken cancel)
        {
            string problem = options.Problem() ?? (string.IsNullOrWhiteSpace(options.ZipPath) ? "No file to save the pack as." : null);
            if (problem != null)
            {
                throw new ArgumentException(problem);
            }
            var clock = Stopwatch.StartNew();
            var settings = new ExportSettings
            {
                PixelsPerTile = options.PixelsPerTile,
                Supersampling = StageCamera.Supersampling,
                Flight = options.Flight,
                // every avatar is drawn at the engineer's height; an animation too large for the sheets at full
                // resolution is drawn at half instead of stopping the export
                HalveWhenTooLarge = true,
            };
            var extra = new List<string>();

            // before the long render: measure every frame (seconds) and stop now if an animation can't fit Factorio's
            // sprite sheets even at half resolution
            renderer.PaddingTiles = 0.25;
            progress?.Invoke(new ExportProgress("measuring", 0, 0));
            List<AnimationFit> tooLarge = new AvatarExporter(poses, renderer, settings).CheckFit(cancel).Where(f => !f.Fits).ToList();
            if (tooLarge.Count > 0)
            {
                throw new InvalidOperationException("Too large for Factorio's sprite sheets, even at half resolution: " +
                    string.Join(", ", tooLarge.Select(f => $"{f.Animation} (frames up to {f.Width} x {f.Height} px)")) +
                    ". Something reaches far out from the body (a long tail, wide wings, a big accessory): turn it off " +
                    "in your avatar (untick it in the Hierarchy), press Reload avatar and export again. Nothing was saved.");
            }

            AvatarExporter exporter = null;
            ExportedArt art = null;
            // the measured meshes plus some room; if something is still drawn further out (a wide outline, a tuft
            // drawn past its mesh), once more, measuring what each frame actually draws, and cutting off only what
            // that still misses
            renderer.PaddingTiles = 0.25;
            foreach (bool retry in new[] { false, true })
            {
                renderer.MeasureDrawn = retry;
                settings.AllowCutOff = options.AllowCutOff && retry;
                exporter = new AvatarExporter(poses, renderer, settings);
                try
                {
                    art = exporter.Render(new SynchronousProgress(progress), cancel);
                    break;
                }
                catch (InvalidOperationException e) when (!retry && e.Message.Contains("drew outside the bounds"))
                {
                    extra.Add("something was drawn past the measured meshes (a wide outline, or a part drawn past its " +
                        "mesh); exported again, measuring what each frame draws");
                }
            }

            List<string> halved = art.PixelsPerTile.Where(r => r.Value < options.PixelsPerTile).Select(r => r.Key).ToList();
            if (halved.Count > 0)
            {
                extra.Add("drawn at half resolution (32 pixels per tile, the same size in the game) because they are too " +
                    "large for the sprite sheets at full: " + string.Join(", ", halved));
            }
            if (exporter.CutOff.Count > 0)
            {
                extra.Add($"cut off at the edge of the picture in {exporter.CutOff.Count} frames (exported anyway), " +
                    "first: " + string.Join("; ", exporter.CutOff.Take(5)));
            }

            var content = new PackContent
            {
                Id = options.Id,
                Title = options.Title.Trim(),
                Author = string.IsNullOrWhiteSpace(options.Author) ? null : options.Author.Trim(),
                Version = options.Version,
                SourceAvatar = sourceAvatar,
                GeneratorVersion = ExporterInfo.Version,
            };
            exporter.Fill(content, art);

            string zip = Path.GetFullPath(options.ZipPath);
            Directory.CreateDirectory(Path.GetDirectoryName(zip));
            string partial = zip + ".partial";
            using (FileStream stream = File.Create(partial))
            {
                new PackWriter(PngWriter.Encode).Write(content, new ZipPackOutput(stream));
            }
            if (File.Exists(zip))
            {
                File.Delete(zip);
            }
            File.Move(partial, zip);

            var report = new StringBuilder();
            foreach (string note in notes.Concat(extra))
            {
                report.AppendLine(note);
            }
            report.AppendLine($"pack: {zip}");
            report.AppendLine($"mod: {PackNaming.ModName(content.Id)} {content.Version}, \"{content.Title}\", " +
                $"{options.PixelsPerTile} pixels per tile, {(options.Flight ? "with" : "without")} flight");
            long bytes = 0;
            int files = 0;
            Directory.CreateDirectory(options.PreviewFolder);
            foreach (KeyValuePair<string, Dictionary<string, SheetSet>> animation in art.Animations)
            {
                foreach (SheetSet sheets in animation.Value.Values)
                {
                    foreach (Image8 file in sheets.Files)
                    {
                        bytes += 4L * file.Width * file.Height;
                        files++;
                    }
                }
                SheetSet character = animation.Value[PackLayers.Base];
                report.AppendLine($"{animation.Key}: {character.DirectionCount} directions x {character.FrameCount} frames of " +
                    $"{character.FrameWidth}x{character.FrameHeight} px");
                for (int i = 0; i < character.Files.Count; i++)
                {
                    string name = animation.Key + (character.Files.Count > 1 ? "-" + (i + 1) : "") + ".png";
                    File.WriteAllBytes(Path.Combine(options.PreviewFolder, name), PngWriter.Encode(character.Files[i]));
                }
            }
            // Factorio compresses sprites and trims their empty edges: 12 test packs of 89 MB each added about 120 MB
            report.AppendLine($"{files} sprite sheets, {bytes / (1024 * 1024)} MB uncompressed (about " +
                $"{bytes / (10 * 1024 * 1024)} MB of video memory in the game)");
            report.AppendLine($"done in {clock.Elapsed.TotalSeconds:0} s");
            File.WriteAllText(Path.Combine(options.PreviewFolder, "report.txt"), report.ToString());
            return new PackExportResult { ZipPath = zip, Report = report.ToString(), CutOff = exporter.CutOff };
        }

        private sealed class SynchronousProgress : IProgress<ExportProgress>
        {
            private readonly Action<ExportProgress> report;

            public SynchronousProgress(Action<ExportProgress> report)
            {
                this.report = report;
            }

            public void Report(ExportProgress value) => report?.Invoke(value);
        }
    }
}

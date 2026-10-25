using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using FactorioAvatarExporter.Core;

namespace FactorioAvatarExporter.PackHarness
{
    /// <summary>
    /// Writes an avatar pack exactly like the Unity exporter will, but from a synthetic rig drawn by the
    /// software stick-figure renderer, so the whole pipeline can be tested without Unity.
    ///
    ///   dotnet run --project unity/dotnet/PackHarness -- --out DIR [--id ID] [--title TITLE]
    ///       [--rig generic|chibi|minimal|dragon] [--height METERS] [--size PERCENT] [--ppt 64|32] [--supersampling N]
    ///       [--no-flight] [--zip]
    ///
    /// Writes DIR/vrc-avatar-ID/ and, with --zip, DIR/vrc-avatar-ID_VERSION.zip.
    ///
    ///   dotnet run --project unity/dotnet/PackHarness -- bundle --out DIR --name NAME [--version V] ZIP...
    ///
    /// combines pack and bundle zips into DIR/avatar-bundle-ID_V.zip, as the exporter's Combine Packs does.
    /// </summary>
    public static class Program
    {
        private const string Version = "1.0.0";

        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "bundle")
            {
                return Bundle(args);
            }
            Options options;
            try
            {
                options = Options.Parse(args);
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine(Options.Usage);
                return 2;
            }

            IPoseSource poses = PoseSource(options);
            var settings = new ExportSettings
            {
                PixelsPerTile = options.PixelsPerTile,
                Supersampling = options.Supersampling,
                Flight = options.Flight,
            };
            var exporter = new AvatarExporter(poses, new StickFigureRenderer(), settings);
            var clock = Stopwatch.StartNew();
            int lastPercent = -1;
            var progress = new SynchronousProgress(p =>
            {
                int percent = (int)(p.Fraction * 100);
                if (percent / 10 != lastPercent / 10)
                {
                    Console.WriteLine($"  {percent,3}% ({p.Animation})");
                    lastPercent = percent;
                }
            });
            ExportedArt art = exporter.Render(progress);

            var content = new PackContent
            {
                Id = options.Id,
                Title = options.Title,
                Author = "pack harness",
                Version = Version,
                SourceAvatar = "synthetic " + options.Rig + " rig, drawn as a stick figure",
                Permission = "Synthetic test figure made by unity/dotnet/PackHarness; no avatar is involved, share freely.",
                GeneratorName = "factorio-avatar-exporter-harness",
                GeneratorVersion = Version,
            };
            exporter.Fill(content, art);

            var writer = new PackWriter(PngWriter.Encode);
            var folder = new DirectoryPackOutput(options.OutputDirectory, true);
            writer.Write(content, folder);
            Console.WriteLine("wrote " + folder.PackDirectory);
            if (options.Zip)
            {
                string zipPath = Path.Combine(options.OutputDirectory, PackNaming.ZipFileName(options.Id, Version));
                using (FileStream stream = File.Create(zipPath))
                {
                    writer.Write(content, new ZipPackOutput(stream));
                }
                Console.WriteLine("wrote " + zipPath);
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "done in {0:0.0} s", clock.Elapsed.TotalSeconds));
            return 0;
        }

        /// <summary>
        /// How tall the figure is drawn, tiles: <see cref="Options.Size"/> of the engineer. The rig's own height in
        /// meters (--height) doesn't change the picture, as in the exporter.
        /// </summary>
        private static double Tall(Options options) => Conventions.EngineerHeight * options.Size / 100;

        private static IPoseSource PoseSource(Options options)
        {
            switch (options.Rig)
            {
                case "dragon":
                    return new DragonPoseSource(BodyShape.Male);
                case "chibi":
                    return new HumanoidAnimations(HumanoidRig.Measure(SyntheticRigs.Chibi(options.Height), Tall(options)));
                case "minimal":
                    return new HumanoidAnimations(HumanoidRig.Measure(SyntheticRigs.Generic(options.Height, null, true), Tall(options)));
                default:
                    return new HumanoidAnimations(HumanoidRig.Measure(SyntheticRigs.Generic(options.Height), Tall(options)));
            }
        }

        private static int Bundle(string[] args)
        {
            string output = null, name = null, version = Version;
            var inputs = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--out": output = args[++i]; break;
                    case "--name": name = args[++i]; break;
                    case "--version": version = args[++i]; break;
                    default: inputs.Add(args[i]); break;
                }
            }
            if (output == null || name == null || inputs.Count == 0)
            {
                Console.Error.WriteLine("usage: PackHarness bundle --out DIR --name NAME [--version V] ZIP...");
                return 2;
            }
            var read = new List<BundleInput>();
            foreach (string input in inputs)
            {
                using (FileStream stream = File.OpenRead(input))
                {
                    read.Add(PackBundler.Read(stream, Path.GetFileName(input)));
                }
            }
            var notes = new List<string>();
            List<BundleCharacter> characters = PackBundler.Merge(read, notes);
            notes.ForEach(n => Console.WriteLine("  " + n));
            var info = new BundleInfo
            {
                Id = PackNaming.DeriveId(name),
                Title = name,
                Version = version,
                Author = "pack harness",
                GeneratorName = "factorio-avatar-exporter-harness",
                GeneratorVersion = Version,
            };
            Directory.CreateDirectory(output);
            string zip = Path.Combine(output, PackBundler.ZipFileName(info.Id, info.Version));
            using (FileStream stream = File.Create(zip))
            {
                PackBundler.Write(stream, info, characters);
            }
            Console.WriteLine($"wrote {zip} with {string.Join(", ", characters.ConvertAll(c => c.Id))}");
            return 0;
        }

        /// <summary>Reports on the calling thread (System.Progress posts to the thread pool).</summary>
        private sealed class SynchronousProgress : IProgress<ExportProgress>
        {
            private readonly Action<ExportProgress> report;

            public SynchronousProgress(Action<ExportProgress> report)
            {
                this.report = report;
            }

            public void Report(ExportProgress value) => report(value);
        }

        private sealed class Options
        {
            public const string Usage =
                "usage: PackHarness --out DIR [--id ID] [--title TITLE] [--rig generic|chibi|minimal|dragon] " +
                "[--height METERS] [--size PERCENT] [--ppt 64|32] [--supersampling N] [--no-flight] [--zip]";

            private static readonly HashSet<string> Rigs = new HashSet<string> { "generic", "chibi", "minimal", "dragon" };

            public string OutputDirectory { get; private set; }
            public string Id { get; private set; } = "harness";
            public string Title { get; private set; } = "Harness";
            public string Rig { get; private set; } = "generic";
            public double Height { get; private set; } = 1.6;

            /// <summary>How tall the figure is drawn, percent of the engineer's height, as the exporter's Size in game.</summary>
            public double Size { get; private set; } = 100;
            public int PixelsPerTile { get; private set; } = 64;
            public int Supersampling { get; private set; } = 2;
            public bool Flight { get; private set; } = true;
            public bool Zip { get; private set; }

            public static Options Parse(string[] args)
            {
                var o = new Options();
                for (int i = 0; i < args.Length; i++)
                {
                    string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("missing value for " + args[i]);
                    switch (args[i])
                    {
                        case "--out": o.OutputDirectory = Value(); break;
                        case "--id": o.Id = Value(); break;
                        case "--title": o.Title = Value(); break;
                        case "--rig": o.Rig = Value(); break;
                        case "--height": o.Height = double.Parse(Value(), CultureInfo.InvariantCulture); break;
                        case "--size": o.Size = double.Parse(Value(), CultureInfo.InvariantCulture); break;
                        case "--ppt": o.PixelsPerTile = int.Parse(Value(), CultureInfo.InvariantCulture); break;
                        case "--supersampling": o.Supersampling = int.Parse(Value(), CultureInfo.InvariantCulture); break;
                        case "--no-flight": o.Flight = false; break;
                        case "--zip": o.Zip = true; break;
                        default: throw new ArgumentException("unknown option " + args[i]);
                    }
                }
                if (string.IsNullOrEmpty(o.OutputDirectory))
                {
                    throw new ArgumentException("--out is required");
                }
                if (!Rigs.Contains(o.Rig))
                {
                    throw new ArgumentException("unknown rig " + o.Rig);
                }
                if (!PackNaming.IsValidId(o.Id))
                {
                    throw new ArgumentException("invalid pack id " + o.Id);
                }
                Directory.CreateDirectory(o.OutputDirectory);
                return o;
            }
        }
    }
}

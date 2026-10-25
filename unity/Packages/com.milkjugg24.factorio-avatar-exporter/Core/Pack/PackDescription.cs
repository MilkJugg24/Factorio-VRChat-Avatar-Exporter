using System;
using System.Collections.Generic;

namespace MilkJugg24.FactorioAvatarExporter.Core
{
    /// <summary>The pack description, prototypes/avatar-pack.lua (docs/vrc-avatar-format.md).</summary>
    public static class PackDescription
    {
        /// <summary>The whole file: a comment naming the generator, then "return" and the table.</summary>
        public static string FileText(PackContent content)
        {
            string table = LuaWriter.Serialize(Table(content));
            return "-- Written by " + content.GeneratorName + " " + content.GeneratorVersion + ".\n" + "return " + table + "\n";
        }

        /// <summary>
        /// The description table as <see cref="LuaWriter"/> input. Sheet file names follow
        /// <see cref="PackFiles.Sheet"/>; the content is expected to be valid (<see cref="PackValidator"/>).
        /// </summary>
        public static Dictionary<string, object> Table(PackContent content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            var table = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["format_version"] = PackFormat.FormatVersion,
                ["id"] = content.Id,
                ["title"] = content.Title,
                ["generator"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["name"] = content.GeneratorName,
                    ["version"] = content.GeneratorVersion,
                },
                ["icon"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["filename"] = PackFiles.ModPath(content.Id, PackFiles.Icon),
                    ["size"] = content.Icon.Width,
                },
                ["pixels_per_tile"] = content.PixelsPerTile,
                ["tintable"] = content.Tintable,
                ["flight"] = content.Flight,
                ["timing"] = Timing(content),
                ["animations"] = Animations(content),
            };
            if (!string.IsNullOrEmpty(content.Author))
            {
                table["author"] = content.Author;
            }
            return table;
        }

        private static Dictionary<string, object> Timing(PackContent content)
        {
            PackTiming timing = content.Timing;
            var table = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["distance_per_frame"] = timing.DistancePerFrame,
                ["sprint_distance_per_frame"] = timing.SprintDistancePerFrame,
                ["mine_impact_frame"] = timing.MineImpactFrame,
                ["left_foot_frames"] = IntList(timing.LeftFootFrames),
                ["right_foot_frames"] = IntList(timing.RightFootFrames),
            };
            if (content.AnimationSpeeds.Count > 0)
            {
                var speeds = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, double> speed in content.AnimationSpeeds)
                {
                    speeds.Add(speed.Key, speed.Value);
                }
                table["animation_speed"] = speeds;
            }
            return table;
        }

        private static List<object> IntList(IReadOnlyList<int> values)
        {
            var list = new List<object>(values.Count);
            foreach (int v in values)
            {
                list.Add(v);
            }
            return list;
        }

        private static Dictionary<string, object> Animations(PackContent content)
        {
            var animations = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Dictionary<string, SheetSet>> animation in content.Animations)
            {
                var layers = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, SheetSet> layer in animation.Value)
                {
                    layers.Add(layer.Key, Layer(content.Id, animation.Key, layer.Key, layer.Value));
                }
                // all layers of a valid animation have the same counts
                SheetSet baseLayer = animation.Value[PackLayers.Base];
                var table = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["direction_count"] = baseLayer.DirectionCount,
                    ["frame_count"] = baseLayer.FrameCount,
                    ["layers"] = layers,
                };
                if (content.AnimationPixelsPerTile.TryGetValue(animation.Key, out int ppt) && ppt != content.PixelsPerTile)
                {
                    table["pixels_per_tile"] = ppt;
                }
                animations.Add(animation.Key, table);
            }
            return animations;
        }

        private static Dictionary<string, object> Layer(string id, string animation, string layer, SheetSet sheets)
        {
            var table = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["width"] = sheets.FrameWidth,
                ["height"] = sheets.FrameHeight,
                ["shift"] = new List<object> { sheets.ShiftX, sheets.ShiftY },
            };
            int count = sheets.Files.Count;
            if (count > 1)
            {
                var filenames = new List<object>(count);
                for (int k = 0; k < count; k++)
                {
                    filenames.Add(PackFiles.ModPath(id, PackFiles.Sheet(animation, layer, k, count)));
                }
                table["filenames"] = filenames;
                if (sheets.LinesPerFile.HasValue)
                {
                    table["lines_per_file"] = sheets.LinesPerFile.Value;
                }
                else
                {
                    table["frames_per_file"] = sheets.FramesPerFile.Value;
                }
            }
            else
            {
                table["filename"] = PackFiles.ModPath(id, PackFiles.Sheet(animation, layer, 0, 1));
            }
            return table;
        }
    }
}

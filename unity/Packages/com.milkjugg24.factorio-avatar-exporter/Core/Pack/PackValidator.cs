using System;
using System.Collections.Generic;
using static System.FormattableString;

namespace MilkJugg24.FactorioAvatarExporter.Core
{
    /// <summary>
    /// Checks a <see cref="PackContent"/> against the rules vrc-avatars enforces when it loads a
    /// pack (prototypes/avatar-packs.lua) and those of tools/avatar_pack/check.py, plus what the exporter
    /// itself needs (a permission statement, single-line texts, a 144 x 144 thumbnail).
    /// </summary>
    public static class PackValidator
    {
        /// <summary>Every problem found, as sentences for the user; empty when the pack is valid.</summary>
        public static IReadOnlyList<string> Validate(PackContent content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            var errors = new List<string>();
            CheckTexts(content, errors);
            CheckPictures(content, errors);
            Dictionary<string, int> frameCounts = CheckAnimations(content, errors);
            CheckTiming(content, frameCounts, errors);
            CheckSpeeds(content, errors);
            return errors;
        }

        private static void CheckTexts(PackContent content, List<string> errors)
        {
            if (!PackNaming.IsValidId(content.Id))
            {
                errors.Add(Invariant($"the id \"{content.Id}\" is not valid: use 1 to {PackFormat.MaxIdLength} lowercase letters, digits and -, starting with a letter or digit and not with corpse- or sprint-"));
            }
            if (string.IsNullOrWhiteSpace(content.Title))
            {
                errors.Add("the title is empty");
            }
            else
            {
                int length = PackNaming.TitleLength(content.Title);
                if (length > PackFormat.MaxTitleLength)
                {
                    errors.Add(Invariant($"the title has {length} characters; at most {PackFormat.MaxTitleLength} are allowed"));
                }
                CheckOneLine("the title", content.Title, errors);
            }
            if (!string.IsNullOrEmpty(content.Author))
            {
                CheckOneLine("the author", content.Author, errors);
            }
            if (!PackNaming.IsValidVersion(content.Version))
            {
                errors.Add(Invariant($"the version \"{content.Version}\" must look like 1.0.0 (three numbers from 0 to 65535)"));
            }
            if (string.IsNullOrWhiteSpace(content.SourceAvatar))
            {
                errors.Add("the name of the source avatar is missing");
            }
            else
            {
                CheckOneLine("the source avatar name", content.SourceAvatar, errors);
            }
            if (string.IsNullOrWhiteSpace(content.GeneratorName) || string.IsNullOrWhiteSpace(content.GeneratorVersion))
            {
                errors.Add("the generator name and version are required");
            }
            else
            {
                CheckOneLine("the generator name", content.GeneratorName, errors);
                CheckOneLine("the generator version", content.GeneratorVersion, errors);
            }
            if (content.PixelsPerTile != 64 && content.PixelsPerTile != 32)
            {
                errors.Add(Invariant($"pixels per tile is {content.PixelsPerTile}; only 64 and 32 are supported"));
            }
            foreach (KeyValuePair<string, int> animation in content.AnimationPixelsPerTile)
            {
                if (animation.Value != 64 && animation.Value != 32)
                {
                    errors.Add(Invariant($"animation \"{animation.Key}\" has pixels per tile {animation.Value}; only 64 and 32 are supported"));
                }
                else if (!content.Animations.ContainsKey(animation.Key))
                {
                    errors.Add(Invariant($"pixels per tile is set for \"{animation.Key}\", which the pack does not have"));
                }
            }
        }

        private static void CheckOneLine(string what, string text, List<string> errors)
        {
            foreach (char c in text)
            {
                if (c < 32 || c == 127)
                {
                    errors.Add(what + " must be a single line without control characters");
                    return;
                }
            }
        }

        private static void CheckPictures(PackContent content, List<string> errors)
        {
            Image8 icon = content.Icon;
            if (icon == null)
            {
                errors.Add("the icon is missing");
            }
            else if (icon.Width != icon.Height)
            {
                errors.Add(Invariant($"the icon is {icon.Width}x{icon.Height} px; it must be square"));
            }
            else if (icon.Width > PackFormat.MaxIconSize)
            {
                errors.Add(Invariant($"the icon is {icon.Width}x{icon.Height} px; at most {PackFormat.MaxIconSize}x{PackFormat.MaxIconSize} is allowed"));
            }
            Image8 thumbnail = content.Thumbnail;
            if (thumbnail == null)
            {
                errors.Add("the thumbnail is missing");
            }
            else if (thumbnail.Width != PackFormat.ThumbnailSize || thumbnail.Height != PackFormat.ThumbnailSize)
            {
                errors.Add(Invariant($"the thumbnail is {thumbnail.Width}x{thumbnail.Height} px; it must be {PackFormat.ThumbnailSize}x{PackFormat.ThumbnailSize}"));
            }
        }

        /// <summary>Checks the animation set and each animation; returns the frame count of every
        /// animation whose layers agree on one.</summary>
        private static Dictionary<string, int> CheckAnimations(PackContent content, List<string> errors)
        {
            var frameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            bool tintable = content.Tintable;
            foreach (AnimationInfo info in AnimationSpec.All)
            {
                bool present = content.Animations.TryGetValue(info.Name, out Dictionary<string, SheetSet> layers);
                if (!present && info.Required)
                {
                    errors.Add(Invariant($"the required \"{info.Name}\" animation is missing"));
                }
                else if (!present && content.Flight)
                {
                    errors.Add(Invariant($"Flight is on, but the \"{info.Name}\" animation is missing (flight animations come all together or not at all)"));
                }
                else if (present && !info.Required && !content.Flight)
                {
                    errors.Add(Invariant($"the \"{info.Name}\" animation is a flight animation, but Flight is off"));
                }
                else if (present)
                {
                    int? frames = CheckAnimation(info, layers, tintable, errors);
                    if (frames.HasValue)
                    {
                        frameCounts.Add(info.Name, frames.Value);
                    }
                }
            }
            foreach (string name in Sorted(content.Animations.Keys))
            {
                if (!IsAnimation(name))
                {
                    errors.Add(Invariant($"\"{name}\" is not an animation of the pack format"));
                }
            }
            return frameCounts;
        }

        private static int? CheckAnimation(AnimationInfo info, Dictionary<string, SheetSet> layers, bool tintable,
            List<string> errors)
        {
            string where = Invariant($"animation \"{info.Name}\"");
            if (layers == null || layers.Count == 0)
            {
                errors.Add(where + " has no layers");
                return null;
            }
            var used = new List<string> { PackLayers.Base, PackLayers.Shadow };
            if (tintable)
            {
                used.Add(PackLayers.Mask);
            }
            foreach (string layer in used)
            {
                if (!layers.ContainsKey(layer))
                {
                    errors.Add(Invariant($"{where} is missing its \"{layer}\" layer") +
                        (layer == PackLayers.Mask ? " (other animations have one: masks go in every animation or none)" : ""));
                }
            }
            used.AddRange(PackLayers.OptionalFor(info.Name));

            string firstLayer = null;
            int frames = 0;
            bool framesAgree = true;
            foreach (string layer in Sorted(layers.Keys))
            {
                if (!used.Contains(layer))
                {
                    errors.Add(Invariant($"{where} has a \"{layer}\" layer, which the pack format does not use"));
                    continue;
                }
                SheetSet sheets = layers[layer];
                if (sheets == null)
                {
                    errors.Add(Invariant($"{where} layer \"{layer}\" has no sheets"));
                    continue;
                }
                CheckSheets(Invariant($"{where} layer \"{layer}\""), info, sheets, errors);
                if (firstLayer == null)
                {
                    firstLayer = layer;
                    frames = sheets.FrameCount;
                }
                else if (sheets.FrameCount != frames)
                {
                    errors.Add(Invariant($"{where} layer \"{layer}\" has {sheets.FrameCount} frames, but layer \"{firstLayer}\" has {frames}"));
                    framesAgree = false;
                }
            }
            if (firstLayer == null || !framesAgree)
            {
                return null;
            }
            if (frames > PackFormat.MaxFrames)
            {
                errors.Add(Invariant($"{where} has {frames} frames; at most {PackFormat.MaxFrames} are allowed"));
                return null;
            }
            return frames;
        }

        private static void CheckSheets(string where, AnimationInfo info, SheetSet sheets, List<string> errors)
        {
            if (sheets.DirectionCount != info.DirectionCount)
            {
                errors.Add(Invariant($"{where} has {sheets.DirectionCount} directions; the pack format needs {info.DirectionCount}"));
            }
            for (int k = 0; k < sheets.Files.Count; k++)
            {
                Image8 file = sheets.Files[k];
                if (file.Width > PackFormat.MaxSheetSize || file.Height > PackFormat.MaxSheetSize)
                {
                    errors.Add(Invariant($"{where} file {k + 1} is {file.Width}x{file.Height} px; sheets may be at most {PackFormat.MaxSheetSize} px each way"));
                }
            }
        }

        private static void CheckTiming(PackContent content, Dictionary<string, int> frameCounts, List<string> errors)
        {
            PackTiming timing = content.Timing;
            if (timing == null)
            {
                errors.Add("the timing is missing");
                return;
            }
            double distance = timing.DistancePerFrame;
            if (!(distance > 0 && distance <= PackFormat.MaxDistancePerFrame))
            {
                errors.Add(Invariant($"timing: the distance per frame must be above 0 and at most {PackFormat.MaxDistancePerFrame} tiles, not {distance}"));
            }
            double sprint = timing.SprintDistancePerFrame;
            if (!(sprint > 0 && sprint <= PackFormat.MaxDistancePerFrame))
            {
                errors.Add(Invariant($"timing: the sprint's distance per frame must be above 0 and at most {PackFormat.MaxDistancePerFrame} tiles, not {sprint}"));
            }
            if (frameCounts.TryGetValue(AnimationSpec.Mine, out int mineFrames) &&
                (timing.MineImpactFrame < 1 || timing.MineImpactFrame > mineFrames))
            {
                errors.Add(Invariant($"timing: the mine impact frame {timing.MineImpactFrame} is not a frame of the mine animation (1 to {mineFrames})"));
            }
            bool runKnown = frameCounts.TryGetValue(AnimationSpec.Run, out int runFrames);
            if (runKnown && frameCounts.TryGetValue(AnimationSpec.Sprint, out int sprintFrames) && sprintFrames != runFrames)
            {
                errors.Add(Invariant($"timing: the sprint has {sprintFrames} frames and the run {runFrames}; they must be the same (the feet land on the same frames)"));
            }
            CheckFootFrames("left", timing.LeftFootFrames, runKnown ? runFrames : (int?)null, errors);
            CheckFootFrames("right", timing.RightFootFrames, runKnown ? runFrames : (int?)null, errors);
        }

        private static void CheckFootFrames(string side, IReadOnlyList<int> frames, int? runFrames, List<string> errors)
        {
            if (frames == null || frames.Count == 0)
            {
                errors.Add("timing: the " + side + " foot frames are missing");
                return;
            }
            if (!runFrames.HasValue)
            {
                return;
            }
            foreach (int frame in frames)
            {
                if (frame < 1 || frame > runFrames.Value)
                {
                    errors.Add(Invariant($"timing: {side} foot frame {frame} is not a frame of the run animation (1 to {runFrames.Value})"));
                }
            }
        }

        private static void CheckSpeeds(PackContent content, List<string> errors)
        {
            foreach (string name in Sorted(content.AnimationSpeeds.Keys))
            {
                double speed = content.AnimationSpeeds[name];
                if (!IsAnimation(name))
                {
                    errors.Add(Invariant($"an animation speed is given for \"{name}\", which is not an animation of the pack format"));
                }
                else if (!(speed > 0) || double.IsInfinity(speed))
                {
                    errors.Add(Invariant($"the animation speed of \"{name}\" must be a positive number, not {speed}"));
                }
            }
        }

        private static bool IsAnimation(string name)
        {
            foreach (AnimationInfo info in AnimationSpec.All)
            {
                if (info.Name == name)
                {
                    return true;
                }
            }
            return false;
        }

        private static List<string> Sorted(IEnumerable<string> names)
        {
            var list = new List<string>(names);
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }
}

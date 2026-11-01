using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using FactorioAvatarExporter.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using AnimationInfo = FactorioAvatarExporter.Core.AnimationInfo;
using Debug = UnityEngine.Debug;

namespace FactorioAvatarExporter.Editor
{
    /// <summary>The exporter in Unity's batch mode, for tests/run_unity_export.ps1.</summary>
    public static class ExporterCommandLine
    {
        /// <summary>
        /// <c>Unity -batchmode -projectPath &lt;project&gt; -executeMethod
        /// FactorioAvatarExporter.Editor.ExporterCommandLine.RenderPoses [-exportScene &lt;scene&gt;]
        /// [-exportAvatar &lt;prefab or object name&gt;] [-exportOut &lt;folder&gt;]</c>: for each avatar (a prefab, a scene object,
        /// or every avatar at the top of a scene), renders every animation posed by Core, a row per direction, into
        /// &lt;folder&gt;/&lt;avatar&gt;/&lt;animation&gt;.png with report.txt, writes summary.txt and exits Unity
        /// with 1 when anything failed.
        /// </summary>
        public static void RenderPoses() => ForEachAvatar(PreviewAvatar);

        /// <summary>
        /// <c>... -executeMethod FactorioAvatarExporter.Editor.ExporterCommandLine.TimePreview</c>: times what the
        /// window does for a full preview (every frame of every animation, one direction) with the avatar just
        /// loaded, then again as after a tuning change, and writes the tables to &lt;folder&gt;/&lt;avatar&gt;/timing.txt.
        /// </summary>
        public static void TimePreview() => ForEachAvatar(TimeAvatar);

        /// <summary>
        /// <c>... -executeMethod FactorioAvatarExporter.Editor.ExporterCommandLine.CheckSliders</c>: poses without
        /// rendering; &lt;folder&gt;/&lt;avatar&gt;/sliders.txt says how far each tuning slider, at each end of its
        /// range, moves each part, and how the wings move per animation.
        /// </summary>
        public static void CheckSliders() => ForEachAvatar(CheckAvatarSliders);

        /// <summary>
        /// <c>Unity -projectPath &lt;project&gt; -executeMethod ...ExporterCommandLine.CheckWindow -exportScene &lt;scene&gt;
        /// -exportOut &lt;folder&gt;</c>, not in batch mode: opens the real window on the scene's first avatar, loads it, drags
        /// sliders with mouse events, opens every "?" and exports, then quits. Screenshots and whatever the Console
        /// got (window-console.txt) go to the folder.
        /// </summary>
        public static void CheckWindow()
        {
            string output = Argument("-exportOut");
            Directory.CreateDirectory(output);
            var console = new StringBuilder();
            int problems = 0;
            Application.logMessageReceivedThreaded += (message, stack, type) =>
            {
                if (type != LogType.Log)
                {
                    lock (console)
                    {
                        problems += type == LogType.Warning ? 0 : 1;
                        console.AppendLine($"{type}: {message}\n{stack}");
                    }
                }
            };
            GameObject avatar = FindAvatars(Argument("-exportScene"), Argument("-exportAvatar"))[0];
            IEnumerator<double> steps = WindowSteps(avatar, output);
            double wake = 0;
            EditorApplication.update += () =>
            {
                if (EditorApplication.timeSinceStartup < wake)
                {
                    return;
                }
                bool more = false;
                try
                {
                    more = steps.MoveNext();
                    wake = more ? EditorApplication.timeSinceStartup + steps.Current : 0;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                if (!more)
                {
                    lock (console)
                    {
                        File.WriteAllText(Path.Combine(output, "window-console.txt"), $"errors: {problems}\n{console}");
                        File.WriteAllText(Path.Combine(output, "summary.txt"), $"window check: {problems} Console errors\n");
                    }
                    EditorApplication.Exit(problems == 0 ? 0 : 1);
                }
            };
        }

        /// <summary>The window check's steps, each returning the seconds to wait before the next.</summary>
        private static IEnumerator<double> WindowSteps(GameObject avatar, string output)
        {
            const double frame = 0.2;
            ExporterWindow.Open();
            ExporterWindow window = EditorWindow.GetWindow<ExporterWindow>();
            window.CheckChoose(avatar, "window check");
            window.Focus();
            window.Repaint();
            yield return 1;
            window.Check.Load = true;
            window.CheckAct();
            yield return 2;
            while (window.CheckBusy)
            {
                yield return frame;
            }
            Capture(window, output, "1-loaded");
            var holds = new StringBuilder();
            // each slider with the tuning field it should move and two values to hold it at. The sliders are moved
            // through the window's own change path in its next GUI pass (synthetic mouse events reached the window
            // a drag late); Unity's slider control itself is left to a check by hand. Lean and Stride change only
            // some animations, so the preview should narrow to those while they are held.
            foreach ((string slider, string field, double first, double second) in new[]
            {
                ("Lean", "Lean", 1.3, 1.6), ("Head tilt", "HeadTilt", 10.0, 20.0), ("Stride", "Stride", 1.15, 1.3),
                ("Flap", "WingFlap", 1.3, 1.6), ("Sway", "TailSway", 1.3, 1.6),
            })
            {
                window.CheckScrollTo(slider);
                window.Repaint();
                yield return 1;
                PoseTuning before = window.CheckTuning;
                window.Check.Holding = true;
                foreach (double value in new[] { first, second })
                {
                    window.Check.Slider = slider;
                    window.Check.SliderValue = value;
                    window.CheckAct();
                    window.Repaint();
                    yield return frame;
                    if (window.Check.Slider != null)
                    {
                        Debug.LogError($"Window check: the {slider} slider wasn't drawn in the check pass.");
                        window.Check.Slider = null;
                    }
                }
                yield return 0.5;
                Capture(window, output, "2-holding-" + slider.Replace(' ', '-'));
                holds.AppendLine($"{slider}: {window.CheckHoldState()}");
                string[] moved = typeof(PoseTuning).GetFields()
                    .Where(f => !Equals(f.GetValue(before), f.GetValue(window.CheckTuning))).Select(f => f.Name).ToArray();
                if (moved.Length != 1 || moved[0] != field)
                {
                    Debug.LogError($"Window check: holding {slider} moved [{string.Join(", ", moved)}], not {field}.");
                }
                window.Check.Holding = false;
                yield return 1;
                holds.AppendLine($"{slider} let go: {window.CheckHoldState()}");
                while (window.CheckBusy)
                {
                    yield return frame;
                }
            }
            File.WriteAllText(Path.Combine(output, "window-holds.txt"), holds.ToString());
            window.Check.OpenHelp = true;
            window.CheckAct();
            window.Check.OpenHelp = false;
            window.Repaint();
            yield return 1;
            Capture(window, output, "3-all-help");
            foreach (string slider in new[] { "Lean", "Flap", "Author" })
            {
                window.CheckScrollTo(slider);
                window.Repaint();
                yield return 1;
                Capture(window, output, "4-help-at-" + slider.Replace(' ', '-'));
            }
            window.Check.ExportTo = Path.Combine(output, "window-export.zip");
            window.CheckAct();
            yield return 2;
            Capture(window, output, "5-exported");
            window.Close();
            yield return 1;
        }

        private static void Capture(EditorWindow window, string output, string name)
        {
            Rect r = window.position;
            float scale = EditorGUIUtility.pixelsPerPoint;
            int w = (int)(r.width * scale), h = (int)(r.height * scale);
            Color[] pixels = InternalEditorUtility.ReadScreenPixel(r.position * scale, w, h);
            var image = new Texture2D(w, h, TextureFormat.RGB24, false);
            image.SetPixels(pixels);
            image.Apply();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }

        /// <summary>
        /// <c>... -executeMethod FactorioAvatarExporter.Editor.ExporterCommandLine.ExportPacks</c>, with the
        /// same arguments as <see cref="RenderPoses"/>: exports each avatar as an avatar pack, the zip, report.txt
        /// and a preview sheet per animation in &lt;folder&gt;/&lt;avatar&gt;/. For testing: the author is
        /// run_unity_export.ps1.
        /// </summary>
        public static void ExportPacks() => ForEachAvatar(ExportPack);

        /// <summary>Runs <paramref name="export"/>(avatar, its folder) for every avatar the arguments name.</summary>
        private static void ForEachAvatar(Func<GameObject, string, string> export)
        {
            string[] args = Environment.GetCommandLineArgs();
            string Arg(string name)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            string output = Arg("-exportOut") ?? Path.Combine(Path.GetDirectoryName(Application.dataPath), "FactorioAvatarExport");
            var summary = new StringBuilder();
            int failed = 0;
            try
            {
                foreach (GameObject source in FindAvatars(Arg("-exportScene"), Arg("-exportAvatar")))
                {
                    summary.AppendLine("== " + source.name);
                    try
                    {
                        summary.Append(export(source, Path.Combine(output, source.name)));
                    }
                    catch (Exception e)
                    {
                        failed++;
                        summary.AppendLine("FAILED: " + e);
                    }
                }
            }
            catch (Exception e)
            {
                failed++;
                summary.AppendLine("FAILED: " + e);
            }
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "summary.txt"), summary.ToString());
            Debug.Log("Factorio Avatar Exporter:\n" + summary);
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }

        private static string PreviewAvatar(GameObject source, string folder)
        {
            string[] args = Environment.GetCommandLineArgs();
            string only = Argument("-exportAnimations");
            using (PosePreview preview = PosePreview.Load(source))
            {
                preview.SetTuning(ParseTuning(Argument("-exportTuning")));
                return preview.SaveAllSheets(folder, null, Array.IndexOf(args, "-exportGuides") >= 0,
                    only?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            }
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>Tuning from "Lean=2,HeadTilt=-30", the names of <see cref="PoseTuning"/>'s fields; null text is the default.</summary>
        internal static PoseTuning ParseTuning(string text)
        {
            var tuning = new PoseTuning();
            foreach (string pair in (text ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = pair.Split('=');
                FieldInfo field = parts.Length == 2 ? typeof(PoseTuning).GetField(parts[0].Trim()) : null;
                if (field == null || field.FieldType != typeof(double))
                {
                    throw new ArgumentException("not a tuning value: " + pair);
                }
                field.SetValue(tuning, double.Parse(parts[1], CultureInfo.InvariantCulture));
            }
            return tuning;
        }

        private static string TimeAvatar(GameObject source, string folder)
        {
            using (PosePreview preview = PosePreview.Load(source))
            {
                const int south = 4;
                var report = new StringBuilder();
                var texture = (Texture2D)null;
                PreviewTimings.Enabled = true;
                try
                {
                    foreach (string title in new[] { "just loaded (the canvases are planned)", "after a tuning change (the canvases are kept)" })
                    {
                        PreviewTimings.Reset();
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        int frames = 0;
                        foreach (AnimationInfo info in AnimationSpec.All)
                        {
                            for (int frame = 0; frame < info.FrameCount; frame++, frames++)
                            {
                                PreviewFrame rendered = preview.Render(info.Name, PosePreview.RowFor(info.Name, south), frame, ExporterWindow.Ground);
                                texture = ExporterWindow.ToTexture(rendered.Image, texture);
                            }
                        }
                        report.AppendLine($"{title}: {frames} frames in {clock.Elapsed.TotalSeconds:0.0} s");
                        report.AppendLine(PreviewTimings.Table(""));
                    }
                    PreviewTimings.Enabled = false;
                    report.AppendLine(CompareQuickFrames(preview, south));
                }
                finally
                {
                    PreviewTimings.Enabled = false;
                    UnityEngine.Object.DestroyImmediate(texture);
                }
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "timing.txt"), report.ToString());
                return report.ToString();
            }
        }

        /// <summary>How far the quick frames are from the full ones, both laid over the window's ground (sRGB, 8 bits).</summary>
        private static string CompareQuickFrames(PosePreview preview, int direction)
        {
            float ground = Mathf.Round(ExporterWindow.Ground.r * 255);
            long sum = 0, count = 0, large = 0;
            int worst = 0;
            foreach (AnimationInfo info in AnimationSpec.All)
            {
                int row = PosePreview.RowFor(info.Name, direction);
                for (int frame = 0; frame < info.FrameCount; frame += Math.Max(1, info.FrameCount / 4))
                {
                    Image8 full = preview.Render(info.Name, row, frame).Image;
                    Image8 quick = preview.Render(info.Name, row, frame, ExporterWindow.Ground).Image;
                    for (int y = 0; y < full.Height; y++)
                    {
                        for (int x = 0; x < full.Width; x++)
                        {
                            if (full[x, y, 3] == 0 && quick[x, y, 3] == 0)
                            {
                                continue;
                            }
                            int most = 0;
                            for (int c = 0; c < 3; c++)
                            {
                                float a = full[x, y, c] * (full[x, y, 3] / 255f) + ground * (1 - full[x, y, 3] / 255f);
                                float b = quick[x, y, c] * (quick[x, y, 3] / 255f) + ground * (1 - quick[x, y, 3] / 255f);
                                most = Math.Max(most, (int)Math.Abs(a - b));
                            }
                            sum += most;
                            count++;
                            large += most > 16 ? 1 : 0;
                            worst = Math.Max(worst, most);
                        }
                    }
                }
            }
            return $"quick frames against full frames, on the ground: mean {(double)sum / Math.Max(1, count):0.00} of 255, worst {worst}, " +
                $"{100.0 * large / Math.Max(1, count):0.00}% of drawn pixels differ by more than 16";
        }

        // each slider's two ends, as the window's ranges
        private static readonly (string field, double low, double high)[] SliderRanges =
        {
            ("Lean", 0, 2), ("HeadTilt", -30, 30), ("ArmSpread", -1, 1), ("ArmSwing", 0, 2), ("ElbowBend", 0, 1), ("Stride", 0.5, 1.5),
            ("WingFlap", 0, 2), ("WingRaise", -45, 45), ("WingSweep", -45, 45), ("ArmWingStroke", 0, 1.5), ("ArmWingRoll", -60, 60),
            ("TailSway", 0, 2), ("TailLift", -45, 45),
        };

        private static string CheckAvatarSliders(GameObject source, string folder)
        {
            using (PosePreview preview = PosePreview.Load(source))
            {
                var report = new StringBuilder();
                foreach (string note in preview.Notes)
                {
                    report.AppendLine(note);
                }
                preview.SetTuning(new PoseTuning(), true);
                Dictionary<(string, int), List<(GuidePart part, Vec3[] points)>> standard = AllGuides(preview);
                report.AppendLine();
                report.AppendLine("Wings by animation, tips in tiles relative to the root (model space; x outwards, y forward, z up): " +
                    "outward, forward and height, lowest to highest, over the animation");
                foreach (AnimationInfo info in AnimationSpec.All)
                {
                    var tips = new SortedDictionary<int, List<Vec3>>();
                    for (int frame = 0; frame < info.FrameCount; frame++)
                    {
                        if (standard.TryGetValue((info.Name, frame), out List<(GuidePart part, Vec3[] points)> chains))
                        {
                            int wing = 0;
                            foreach ((GuidePart part, Vec3[] points) in chains)
                            {
                                if (part == GuidePart.Wings)
                                {
                                    Vec3 from = points[points.Length - 1] - points[0];
                                    if (!tips.TryGetValue(wing, out List<Vec3> list))
                                    {
                                        tips[wing] = list = new List<Vec3>();
                                    }
                                    list.Add(new Vec3(Math.Abs(from.X), from.Y, from.Z));
                                    wing++;
                                }
                            }
                        }
                    }
                    if (tips.Count == 0)
                    {
                        report.AppendLine($"{info.Name}: no wings posed");
                    }
                    foreach (KeyValuePair<int, List<Vec3>> wing in tips)
                    {
                        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16} wing {1}: out {2:0.00} to {3:0.00}, forward {4:0.00} to {5:0.00}, height {6:0.00} to {7:0.00}",
                            info.Name, wing.Key, wing.Value.Min(t => t.X), wing.Value.Max(t => t.X), wing.Value.Min(t => t.Y), wing.Value.Max(t => t.Y),
                            wing.Value.Min(t => t.Z), wing.Value.Max(t => t.Z)));
                    }
                }
                report.AppendLine();
                report.AppendLine("Each slider at each end against its default: the part, the furthest a joint moved in tiles, and the mean move of " +
                    "the part's joints (outward, forward, up), then the animations that changed");
                foreach ((string field, double low, double high) in SliderRanges)
                {
                    foreach (double end in new[] { low, high })
                    {
                        preview.SetTuning(ParseTuning($"{field}={end.ToString(CultureInfo.InvariantCulture)}"), true);
                        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} = {1:0.##}", field, end));
                        report.Append(CompareGuides(standard, AllGuides(preview)));
                    }
                }
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "sliders.txt"), report.ToString());
                return report.ToString();
            }
        }

        private static Dictionary<(string, int), List<(GuidePart part, Vec3[] points)>> AllGuides(PosePreview preview)
        {
            var all = new Dictionary<(string, int), List<(GuidePart, Vec3[])>>();
            foreach (AnimationInfo info in AnimationSpec.All)
            {
                for (int frame = 0; frame < info.FrameCount; frame++)
                {
                    all[(info.Name, frame)] = preview.Guides(info.Name, 0, frame);
                }
            }
            return all;
        }

        private static string CompareGuides(Dictionary<(string animation, int frame), List<(GuidePart part, Vec3[] points)>> before,
            Dictionary<(string, int), List<(GuidePart part, Vec3[] points)>> after)
        {
            const double moved = 0.002;
            var furthest = new Dictionary<GuidePart, double>();
            var sums = new Dictionary<GuidePart, (Vec3 sum, int count)>();
            var animations = new Dictionary<GuidePart, HashSet<string>>();
            foreach (KeyValuePair<(string animation, int frame), List<(GuidePart part, Vec3[] points)>> entry in before)
            {
                List<(GuidePart part, Vec3[] points)> now = after[entry.Key];
                for (int c = 0; c < entry.Value.Count && c < now.Count; c++)
                {
                    (GuidePart part, Vec3[] was) = entry.Value[c];
                    Vec3[] points = now[c].points;
                    for (int p = 0; p < was.Length && p < points.Length; p++)
                    {
                        Vec3 delta = points[p] - was[p];
                        double distance = Vec3.Distance(points[p], was[p]);
                        furthest.TryGetValue(part, out double far);
                        furthest[part] = Math.Max(far, distance);
                        if (distance > moved)
                        {
                            sums.TryGetValue(part, out (Vec3 sum, int count) s);
                            sums[part] = (s.sum + new Vec3(was[p].X >= 0 ? delta.X : -delta.X, delta.Y, delta.Z), s.count + 1);
                            if (!animations.TryGetValue(part, out HashSet<string> names))
                            {
                                animations[part] = names = new HashSet<string>();
                            }
                            names.Add(entry.Key.animation);
                        }
                    }
                }
            }
            var text = new StringBuilder();
            foreach (KeyValuePair<GuidePart, (Vec3 sum, int count)> part in sums.OrderBy(s => s.Key))
            {
                Vec3 mean = part.Value.sum * (1.0 / part.Value.count);
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,-6} furthest {1:0.000}; mean out {2:+0.000;-0.000}, forward {3:+0.000;-0.000}, up {4:+0.000;-0.000}; {5}",
                    part.Key, furthest[part.Key], mean.X, mean.Y, mean.Z, string.Join(" ", animations[part.Key].OrderBy(n => n))));
            }
            return text.Length > 0 ? text.ToString() : "  nothing moved\n";
        }

        private static string ExportPack(GameObject source, string folder)
        {
            using (PosePreview preview = PosePreview.Load(source))
            {
                var options = new PackOptions
                {
                    Title = source.name,
                    Id = PackNaming.DeriveId(source.name),
                    Author = "run_unity_export.ps1",
                    ZipPath = Path.Combine(folder, PackNaming.ZipFileName(PackNaming.DeriveId(source.name), ExporterInfo.Version)),
                    PreviewFolder = folder,
                };
                return preview.ExportPack(options, null, CancellationToken.None).Report;
            }
        }

        /// <summary>A prefab, a named top-level object of a scene, or every active humanoid at a scene's top level.</summary>
        internal static List<GameObject> FindAvatars(string scenePath, string avatarName)
        {
            var sources = new List<GameObject>();
            if (avatarName != null && avatarName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(avatarName);
                if (prefab == null)
                {
                    throw new ArgumentException("no prefab at " + avatarName);
                }
                sources.Add(prefab);
            }
            else if (scenePath != null)
            {
                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    Animator animator = root.activeInHierarchy ? root.GetComponent<Animator>() : null;
                    bool humanoid = animator != null && animator.avatar != null && animator.avatar.isHuman;
                    if (avatarName != null ? root.name == avatarName : humanoid)
                    {
                        sources.Add(root);
                    }
                }
            }
            else
            {
                throw new ArgumentException("pass -exportScene <scene>, -exportAvatar <prefab>, or both");
            }
            if (sources.Count == 0)
            {
                throw new ArgumentException("no avatar found" + (avatarName != null ? " named " + avatarName : " in " + scenePath));
            }
            return sources;
        }
    }
}

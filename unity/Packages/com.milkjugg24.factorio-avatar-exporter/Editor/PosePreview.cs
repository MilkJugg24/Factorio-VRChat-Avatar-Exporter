using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MilkJugg24.FactorioAvatarExporter.Core;
using UnityEditor;
using UnityEngine;
using AnimationInfo = MilkJugg24.FactorioAvatarExporter.Core.AnimationInfo;

namespace MilkJugg24.FactorioAvatarExporter.Editor
{
    /// <summary>One rendered frame of an animation, with what the preview knows about it.</summary>
    internal sealed class PreviewFrame
    {
        public Image8 Image;
        public CanvasSpec Canvas;

        /// <summary>How far the posed bones are from Core's joints, final pixels (NaN when not checked).</summary>
        public double JointErrorPixels;
    }

    /// <summary>
    /// Renders an avatar posed by Core's humanoid animations, frame by frame, for the exporter window and
    /// the command line. Each animation gets one canvas for all its directions and frames, planned from the
    /// posed meshes; if a frame still reaches the canvas edge (something the meshes didn't show, such as a wide
    /// outline), the canvas grows and the frame is rendered again.
    /// </summary>
    internal sealed class PosePreview : IDisposable
    {
        public const int PixelsPerTile = 64;
        private const int MarginStep = 16;
        private const int MaxGrowth = 6;

        private readonly AvatarStage stage;
        private readonly StageCamera camera;
        private HumanoidAnimations poses;
        private readonly Dictionary<string, Bounds3> bounds = new Dictionary<string, Bounds3>();
        private readonly Dictionary<string, int> margins = new Dictionary<string, int>();

        private PosePreview(AvatarStage stage)
        {
            this.stage = stage;
            camera = new StageCamera(stage);
            poses = new HumanoidAnimations(stage.Rig);
            Notes.AddRange(stage.Notes);
            Notes.Add($"shaders: compiled {StageCamera.CompileShaders(stage.Avatar)} passes up front");
        }

        public List<string> Notes { get; } = new List<string>();
        public string AvatarName => stage.Avatar.name;

        /// <summary>Times a canvas grew because a frame reached its edge.</summary>
        public int Growths { get; private set; }

        public int Retakes => camera.Retakes;
        public int PlaceholderLeft => camera.PlaceholderLeft;

        /// <summary>How big the avatar is drawn (always the engineer's height), for the window and report.txt.</summary>
        public string SizeNote =>
            $"drawn at the engineer's height, {stage.HeightTiles.ToString("0.00", CultureInfo.InvariantCulture)} tiles tall " +
            $"({stage.MetersPerTile.ToString("0.000", CultureInfo.InvariantCulture)} m per tile)";

        /// <summary>
        /// Whether each animation fits Factorio's sprite sheets at <paramref name="pixelsPerTile"/>, measured as
        /// Export All measures it first (<see cref="AvatarExporter.CheckFit"/>), halving those too large.
        /// </summary>
        public IReadOnlyList<AnimationFit> CheckFit(int pixelsPerTile, bool flight)
        {
            using (var renderer = new StageRenderer(stage, camera))
            {
                var settings = new ExportSettings
                {
                    PixelsPerTile = pixelsPerTile,
                    Supersampling = StageCamera.Supersampling,
                    Flight = flight,
                    HalveWhenTooLarge = true,
                };
                return new AvatarExporter(poses, renderer, settings).CheckFit();
            }
        }

        /// <summary>Lights the avatar for the preview and the export (<see cref="StageCamera.SetLighting"/>).</summary>
        public void SetLighting(StageLighting lighting) => camera.SetLighting(lighting);

        public static PosePreview Load(GameObject source, double heightTiles = Conventions.EngineerHeight,
            IEnumerable<string> leaveOut = null)
        {
            AvatarStage stage = AvatarStage.Load(source, heightTiles, leaveOut);
            try
            {
                return new PosePreview(stage);
            }
            catch
            {
                stage.Dispose();
                throw;
            }
        }

        public PoseResult Pose(string animation, int row, int frame) =>
            poses.Pose(animation, AnimationSpec.FramePhase(animation, frame), AnimationSpec.Direction(animation, row).Move);

        /// <summary>Frame <paramref name="frame"/> of direction <paramref name="row"/> (both from 0).</summary>
        public PreviewFrame Render(string animation, int row, int frame)
        {
            Canvas(animation); // planning poses every frame, so it comes first
            PoseResult pose = Pose(animation, row, frame);
            stage.SetOrientation(0);
            stage.ApplyPose(pose);
            double error = pose.GroundToLowestPoint ? double.NaN : stage.JointError(pose) * PixelsPerTile;
            stage.SetOrientation(AnimationSpec.Direction(animation, row).Orientation);
            for (int growth = 0; ; growth++)
            {
                CanvasSpec canvas = Canvas(animation);
                camera.Aim(canvas);
                Image8 image = ImageConvert.ToImage8(camera.RenderFrame());
                if (!StageCamera.TouchesEdge(image) || growth == MaxGrowth)
                {
                    return new PreviewFrame { Image = image, Canvas = canvas, JointErrorPixels = error };
                }
                margins[animation] = Margin(animation) + MarginStep;
                Growths++;
            }
        }

        private int Margin(string animation) =>
            margins.TryGetValue(animation, out int margin) ? margin : CanvasPlanner.DefaultMarginPixels;

        /// <summary>
        /// The animation's canvas: the posed meshes of every frame (as the first direction moves), turned to every
        /// direction, plus the margin.
        /// </summary>
        private CanvasSpec Canvas(string animation)
        {
            if (!bounds.TryGetValue(animation, out Bounds3 box))
            {
                AnimationInfo info = AnimationSpec.Get(animation);
                box = Bounds3.Empty;
                for (int frame = 0; frame < info.FrameCount; frame++)
                {
                    stage.SetOrientation(0);
                    stage.ApplyPose(Pose(animation, 0, frame));
                    List<Vector3> points = stage.MeshPoints();
                    for (int row = 0; row < info.DirectionCount; row++)
                    {
                        Quaternion turn = Quaternion.Euler(0, (float)(360 * AnimationSpec.Direction(animation, row).Orientation), 0);
                        foreach (Vector3 p in points)
                        {
                            box = box.Encapsulate(AvatarStage.ToCore(turn * p) * stage.Rig.TilesPerMeter);
                        }
                    }
                }
                bounds[animation] = box;
            }
            return CanvasPlanner.PlanColor(new[] { box }, PixelsPerTile, StageCamera.Supersampling, Margin(animation));
        }

        /// <summary>Every frame of every direction of an animation in one image: a row per direction.</summary>
        public Image8 Sheet(string animation, out double worstJointErrorPixels, Action<float> progress = null)
        {
            AnimationInfo info = AnimationSpec.Get(animation);
            var frames = new PreviewFrame[info.DirectionCount, info.FrameCount];
            worstJointErrorPixels = 0;
            int done = 0;
            // render twice if the canvas grew on the way, so every frame shares the final canvas
            for (int pass = 0; pass < 2; pass++)
            {
                int growthsBefore = Growths;
                for (int row = 0; row < info.DirectionCount; row++)
                {
                    for (int frame = 0; frame < info.FrameCount; frame++)
                    {
                        frames[row, frame] = Render(animation, row, frame);
                        if (!double.IsNaN(frames[row, frame].JointErrorPixels))
                        {
                            worstJointErrorPixels = Math.Max(worstJointErrorPixels, frames[row, frame].JointErrorPixels);
                        }
                        progress?.Invoke((float)++done / (2 * info.DirectionCount * info.FrameCount));
                    }
                }
                if (Growths == growthsBefore)
                {
                    break;
                }
            }
            int w = frames[0, 0].Image.Width, h = frames[0, 0].Image.Height;
            var sheet = new Image8(w * info.FrameCount, h * info.DirectionCount);
            for (int row = 0; row < info.DirectionCount; row++)
            {
                for (int frame = 0; frame < info.FrameCount; frame++)
                {
                    Image8 image = frames[row, frame].Image;
                    for (int y = 0; y < Math.Min(h, image.Height); y++)
                    {
                        for (int x = 0; x < Math.Min(w, image.Width); x++)
                        {
                            for (int c = 0; c < 4; c++)
                            {
                                sheet[frame * w + x, row * h + y, c] = image[x, y, c];
                            }
                        }
                    }
                }
            }
            return sheet;
        }

        /// <summary>
        /// Writes the <see cref="Sheet"/> of every animation to <paramref name="folder"/> as
        /// &lt;animation&gt;.png, and report.txt: the notes, and each animation's size and pose check. Returns
        /// the report. <paramref name="progress"/> gets the animation being drawn and the part of all done.
        /// </summary>
        public string SaveAllSheets(string folder, Action<string, float> progress = null)
        {
            var clock = Stopwatch.StartNew();
            var report = new StringBuilder();
            Directory.CreateDirectory(folder);
            foreach (string note in Notes)
            {
                report.AppendLine(note);
            }
            IReadOnlyList<AnimationInfo> all = AnimationSpec.All;
            for (int i = 0; i < all.Count; i++)
            {
                AnimationInfo info = all[i];
                int index = i;
                Image8 sheet = Sheet(info.Name, out double worst, p => progress?.Invoke(info.Name, (index + p) / all.Count));
                File.WriteAllBytes(Path.Combine(folder, info.Name + ".png"), PngWriter.Encode(sheet));
                report.AppendLine($"{info.Name}: {info.DirectionCount} directions x {info.FrameCount} frames of " +
                    $"{sheet.Width / info.FrameCount}x{sheet.Height / info.DirectionCount} px; bones within {worst:0.0} px of Core's joints");
            }
            report.AppendLine($"retaken while shaders compiled: {Retakes}; placeholder pixels left: " +
                $"{PlaceholderLeft}; canvas grown to fit: {Growths} times");
            report.AppendLine($"done in {clock.Elapsed.TotalSeconds:0.0} s");
            File.WriteAllText(Path.Combine(folder, "report.txt"), report.ToString());
            return report.ToString();
        }

        /// <summary>
        /// Exports the avatar as an avatar pack (<see cref="PackExport.Write"/>): every animation rendered
        /// with shadows, the zip written to the options' output folder.
        /// </summary>
        /// <summary>
        /// The avatar's objects that draw on the edge of the frame last rendered (as posed, turned and framed now),
        /// as paths below the avatar. Objects are switched on one at a time, siblings off, going down into
        /// those that reach the edge, so renderers, UI canvases and anything else are found, down to the smallest
        /// object that still reaches it. Such an object draws past the canvas whatever its size, like an effect or
        /// a panel drawn at a fixed place on screen. Null when nothing reaches the edge now; empty when the
        /// avatar's objects don't explain it.
        /// </summary>
        public List<string> EdgeDrawers()
        {
            bool Touches() => StageCamera.TouchesEdge(ImageConvert.ToImage8(camera.RenderFrame()));
            if (!Touches())
            {
                return null;
            }
            Transform root = stage.Avatar.transform;
            var found = new List<string>();

            void Search(Transform parent, int depth)
            {
                List<GameObject> children = parent.Cast<Transform>().Select(t => t.gameObject).Where(g => g.activeSelf).ToList();
                foreach (GameObject child in children)
                {
                    child.SetActive(false);
                }
                try
                {
                    if (parent != root && Touches())
                    {
                        // the object itself, without its children
                        found.Add(AnimationUtility.CalculateTransformPath(parent, root));
                        return;
                    }
                    foreach (GameObject child in children)
                    {
                        child.SetActive(true);
                        if (Touches())
                        {
                            int before = found.Count;
                            if (depth < 12)
                            {
                                Search(child.transform, depth + 1);
                            }
                            if (found.Count == before)
                            {
                                found.Add(AnimationUtility.CalculateTransformPath(child.transform, root));
                            }
                        }
                        child.SetActive(false);
                    }
                }
                finally
                {
                    foreach (GameObject child in children)
                    {
                        child.SetActive(true);
                    }
                }
            }

            Search(root, 0);
            return found.Distinct().ToList();
        }

        /// <summary>What on the avatar may draw outside its measured meshes (<see cref="AvatarStage.DrawnOutsideMeshes"/>).</summary>
        public List<string> DrawnOutsideMeshes() => stage.DrawnOutsideMeshes();

        public PackExportResult ExportPack(PackOptions options, Action<ExportProgress> progress, CancellationToken cancel)
        {
            using (var renderer = new StageRenderer(stage, camera))
            {
                return PackExport.Write(options, AvatarName, poses, renderer, Notes.Append(SizeNote).Append(camera.Lighting.Describe()),
                    progress, cancel);
            }
        }

        /// <summary>The name of a sheet row, e.g. "south" or "gun north, moving east".</summary>
        public static string DirectionName(string animation, int row)
        {
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            AnimationInfo info = AnimationSpec.Get(animation);
            if (info.GunMoveRows)
            {
                (int gun, int move) = AnimationSpec.GunMoveDirections[row];
                return $"gun {names[gun % 8]}, moving {names[move % 8]}";
            }
            return info.DirectionCount == 8 ? names[row] : "the one direction";
        }

        public void Dispose()
        {
            camera.Dispose();
            stage.Dispose();
        }
    }
}

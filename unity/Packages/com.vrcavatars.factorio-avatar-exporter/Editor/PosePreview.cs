using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using FactorioAvatarExporter.Core;
using UnityEditor;
using UnityEngine;
using AnimationInfo = FactorioAvatarExporter.Core.AnimationInfo;

namespace FactorioAvatarExporter.Editor
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

        /// <summary>The pack timing the poses were made with (the run's distance per frame, for the preview's speed).</summary>
        public PackTiming Timing => poses.Timing;

        /// <summary>The animation tuning the poses are made with (the window's sliders), for the preview and the export.</summary>
        public PoseTuning Tuning { get; private set; } = new PoseTuning();

        /// <summary>
        /// Poses the avatar with <paramref name="tuning"/> from now on; frames rendered before are out of date.
        /// <paramref name="keepCanvases"/> keeps each animation's canvas as it is (a frame that no longer fits grows it
        /// when rendered), so the window draws again only the frames the tuning changed.
        /// </summary>
        public void SetTuning(PoseTuning tuning, bool keepCanvases = false)
        {
            Tuning = (tuning ?? new PoseTuning()).Clamped();
            poses = new HumanoidAnimations(stage.Rig, Tuning);
            if (!keepCanvases)
            {
                // the poses decide each animation's canvas
                bounds.Clear();
            }
        }

        /// <summary>
        /// Whether a frame posed by <paramref name="a"/> looks the same as one posed by <paramref name="b"/> on this
        /// avatar (<see cref="PoseResult.SameAs"/>, leaving out the wings or tail it doesn't have).
        /// </summary>
        public bool LooksSame(PoseResult a, PoseResult b) => a.SameAs(b, wings: stage.HasWings, tail: stage.HasTail);

        /// <summary>Whether the avatar has wing bones that move something visible (the flight method's default).</summary>
        public bool HasWings => stage.HasWings;

        /// <summary>The poses the frames are rendered from now, to compare with those of another tuning.</summary>
        public HumanoidAnimations Poses => poses;

        public static PoseResult Pose(HumanoidAnimations poses, string animation, int row, int frame) =>
            poses.Pose(animation, AnimationSpec.FramePhase(animation, frame), AnimationSpec.Direction(animation, row).Move);

        /// <summary>
        /// The guide lines of a frame (<see cref="AvatarStage.GuideChains"/>) as the avatar is posed now, in Core's
        /// model space; none for the corpse, which is dropped onto the ground after posing.
        /// </summary>
        public List<(GuidePart part, Vec3[] points)> Guides(string animation, int row, int frame)
        {
            PoseResult pose = Pose(animation, row, frame);
            if (pose.GroundToLowestPoint)
            {
                return new List<(GuidePart, Vec3[])>();
            }
            stage.SetOrientation(0);
            stage.ApplyPose(pose);
            return stage.GuideChains();
        }

        /// <summary>The colour of each part's guide lines.</summary>
        public static readonly IReadOnlyDictionary<GuidePart, Color> GuideColors = new Dictionary<GuidePart, Color>
        {
            { GuidePart.Spine, new Color(1f, 0.55f, 0.1f) },
            { GuidePart.Head, new Color(1f, 1f, 1f) },
            { GuidePart.Arms, new Color(0.2f, 0.85f, 1f) },
            { GuidePart.Legs, new Color(0.45f, 1f, 0.3f) },
            { GuidePart.Wings, new Color(1f, 0.35f, 0.9f) },
            { GuidePart.Tail, new Color(1f, 0.25f, 0.25f) },
        };

        /// <summary>Where a guide point of a model facing <paramref name="orientation"/> is on <paramref name="canvas"/>, in pixels.</summary>
        public static (double x, double y) GuidePixel(Vec3 point, double orientation, CanvasSpec canvas) =>
            FactorioCamera.Project(Conventions.ModelPointToWorld(point, orientation, false), canvas);

        /// <summary>
        /// Draws the guide lines of a rendered frame onto it, the window's lines on the command line's sheets, and
        /// adds to <paramref name="check"/> how they agree with the camera and the picture.
        /// </summary>
        private void DrawGuides(PreviewFrame frame, string animation, int row, int frameIndex, GuideCheck check)
        {
            List<(GuidePart part, Vec3[] points)> chains = Guides(animation, row, frameIndex);
            double orientation = AnimationSpec.Direction(animation, row).Orientation;
            stage.SetOrientation(orientation);
            camera.Aim(frame.Canvas);
            // read first: the lines drawn must not count as the avatar
            foreach ((GuidePart part, Vec3[] points) in chains)
            {
                foreach (Vec3 point in points)
                {
                    (double x, double y) = GuidePixel(point, orientation, frame.Canvas);
                    Vector3 world = stage.Pivot.TransformPoint(AvatarStage.ToUnity(point, stage.MetersPerTile));
                    (double cameraX, double cameraY) = camera.CanvasPixel(world, frame.Canvas);
                    check.CameraPixels = Math.Max(check.CameraPixels, Math.Max(Math.Abs(x - cameraX), Math.Abs(y - cameraY)));
                    check.Add(part, row, OnAvatar(frame.Image, x, y), OnAvatar(frame.Image, 2 * frame.Canvas.AnchorX - x, y));
                }
            }
            foreach ((GuidePart part, Vec3[] points) in chains)
            {
                Color color = GuideColors[part];
                for (int i = 0; i + 1 < points.Length; i++)
                {
                    (double ax, double ay) = GuidePixel(points[i], orientation, frame.Canvas);
                    (double bx, double by) = GuidePixel(points[i + 1], orientation, frame.Canvas);
                    int steps = (int)Math.Ceiling(Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay))) + 1;
                    for (int s = 0; s <= steps; s++)
                    {
                        Plot(frame.Image, ax + (bx - ax) * s / steps, ay + (by - ay) * s / steps, color);
                    }
                }
            }
        }

        /// <summary>True when the picture is drawn on within a pixel of (x, y): bones lie inside the body, a little off an edge at worst.</summary>
        private static bool OnAvatar(Image8 image, double x, double y)
        {
            int px = (int)Math.Floor(x), py = (int)Math.Floor(y);
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ix = px + dx, iy = py + dy;
                    if (ix >= 0 && iy >= 0 && ix < image.Width && iy < image.Height && image[ix, iy, 3] != 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void Plot(Image8 image, double x, double y, Color color)
        {
            int px = (int)Math.Floor(x), py = (int)Math.Floor(y);
            if (px >= 0 && py >= 0 && px < image.Width && py < image.Height)
            {
                image[px, py, 0] = (byte)(color.r * 255);
                image[px, py, 1] = (byte)(color.g * 255);
                image[px, py, 2] = (byte)(color.b * 255);
                image[px, py, 3] = 255;
            }
        }

        /// <summary>How an animation's guide lines agree with the camera and with the picture, for report.txt.</summary>
        private sealed class GuideCheck
        {
            private readonly Dictionary<(GuidePart part, int row), (int on, int mirrored, int all)> counts =
                new Dictionary<(GuidePart, int), (int, int, int)>();

            /// <summary>The furthest a guide point is from where the camera shows its bone, canvas pixels.</summary>
            public double CameraPixels { get; set; }

            public void Add(GuidePart part, int row, bool on, bool mirrored)
            {
                counts.TryGetValue((part, row), out (int on, int mirrored, int all) c);
                counts[(part, row)] = (c.on + (on ? 1 : 0), c.mirrored + (mirrored ? 1 : 0), c.all + 1);
            }

            public string Describe(string animation)
            {
                var parts = new List<string>();
                foreach (IGrouping<GuidePart, KeyValuePair<(GuidePart part, int row), (int on, int mirrored, int all)>> part
                    in counts.GroupBy(c => c.Key.part).OrderBy(g => g.Key))
                {
                    int on = part.Sum(c => c.Value.on), mirrored = part.Sum(c => c.Value.mirrored), all = part.Sum(c => c.Value.all);
                    var worst = part.OrderBy(c => (double)c.Value.on / c.Value.all).First();
                    parts.Add($"{part.Key} {100 * on / all}% (mirrored {100 * mirrored / all}%; worst {DirectionName(animation, worst.Key.row)} " +
                        $"{100 * worst.Value.on / worst.Value.all}%)");
                }
                return $"guides: within {CameraPixels:0.00} px of the camera; joints on the avatar: " +
                    (parts.Count > 0 ? string.Join(", ", parts) : "none drawn");
            }
        }

        /// <summary>
        /// The sheet row of <paramref name="animation"/> facing <paramref name="direction"/> (0 north, clockwise, 8
        /// directions): the same row for 8-direction animations, the gun-and-moving row facing that way (or its
        /// mirror image, which the game flips, for the west half) for the 18-direction ones, row 0 for the corpse.
        /// </summary>
        public static int RowFor(string animation, int direction)
        {
            AnimationInfo info = AnimationSpec.Get(animation);
            if (info.GunMoveRows)
            {
                int facing = direction <= 4 ? direction : 8 - direction;
                for (int r = 0; r < AnimationSpec.GunMoveDirections.Count; r++)
                {
                    if (AnimationSpec.GunMoveDirections[r] == (facing, facing))
                    {
                        return r;
                    }
                }
                return 0;
            }
            return info.DirectionCount == 8 ? direction : 0;
        }

        /// <summary>Lights the avatar for the preview and the export (<see cref="StageCamera.SetLighting"/>).</summary>
        public void SetLighting(StageLighting lighting) => camera.SetLighting(lighting);

        public static PosePreview Load(GameObject source, double heightTiles = Conventions.EngineerHeight,
            IEnumerable<string> leaveOut = null, BoneChoices choices = null)
        {
            AvatarStage stage = AvatarStage.Load(source, heightTiles, leaveOut, choices);
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

        public PoseResult Pose(string animation, int row, int frame) => Pose(poses, animation, row, frame);

        /// <summary>Frame <paramref name="frame"/> of direction <paramref name="row"/> (both from 0).</summary>
        public PreviewFrame Render(string animation, int row, int frame) => Render(animation, row, frame, null);

        /// <summary>
        /// As <see cref="Render(string, int, int)"/>; with <paramref name="quickGround"/>, the quick frame for
        /// drawing on that colour (<see cref="StageCamera.RenderPreviewFrame"/>).
        /// </summary>
        public PreviewFrame Render(string animation, int row, int frame, Color? quickGround)
        {
            using (PreviewTimings.Time("Render (whole frame)"))
            {
                Canvas(animation); // planning poses every frame, so it comes first
                PoseResult pose;
                using (PreviewTimings.Time("core pose"))
                {
                    pose = Pose(animation, row, frame);
                }
                stage.SetOrientation(0);
                using (PreviewTimings.Time("ApplyPose"))
                {
                    stage.ApplyPose(pose);
                }
                double error;
                using (PreviewTimings.Time("JointError"))
                {
                    error = pose.GroundToLowestPoint ? double.NaN : stage.JointError(pose) * PixelsPerTile;
                }
                stage.SetOrientation(AnimationSpec.Direction(animation, row).Orientation);
                for (int growth = 0; ; growth++)
                {
                    CanvasSpec canvas = Canvas(animation);
                    camera.Aim(canvas);
                    ImageF rendered = quickGround.HasValue ? camera.RenderPreviewFrame(quickGround.Value) : camera.RenderFrame();
                    Image8 image;
                    using (PreviewTimings.Time("ToImage8"))
                    {
                        image = ImageConvert.ToImage8(rendered);
                    }
                    if (!StageCamera.TouchesEdge(image) || growth == MaxGrowth)
                    {
                        return new PreviewFrame { Image = image, Canvas = canvas, JointErrorPixels = error };
                    }
                    margins[animation] = Margin(animation) + MarginStep;
                    Growths++;
                }
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
                using (PreviewTimings.Time("canvas planning (animation)"))
                {
                    AnimationInfo info = AnimationSpec.Get(animation);
                    box = Bounds3.Empty;
                    for (int frame = 0; frame < info.FrameCount; frame++)
                    {
                        stage.SetOrientation(0);
                        using (PreviewTimings.Time("planning: ApplyPose"))
                        {
                            stage.ApplyPose(Pose(animation, 0, frame));
                        }
                        List<Vector3> points;
                        using (PreviewTimings.Time("planning: MeshPoints"))
                        {
                            points = stage.MeshPoints();
                        }
                        using (PreviewTimings.Time("planning: turn the points"))
                        {
                            for (int row = 0; row < info.DirectionCount; row++)
                            {
                                box = TurnedBounds(box, points, Quaternion.Euler(0, (float)(360 * AnimationSpec.Direction(animation, row).Orientation), 0));
                            }
                        }
                    }
                    bounds[animation] = box;
                }
            }
            return CanvasPlanner.PlanColor(new[] { box }, PixelsPerTile, StageCamera.Supersampling, Margin(animation));
        }

        /// <summary>
        /// <paramref name="box"/> grown by <paramref name="points"/> (meters) turned about the vertical: only the two
        /// extreme corners reach the box, as there are many thousands of points to turn for every frame and direction.
        /// </summary>
        private Bounds3 TurnedBounds(Bounds3 box, List<Vector3> points, Quaternion turn)
        {
            float xx = 1 - 2 * turn.y * turn.y, xz = 2 * turn.w * turn.y;
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                float x = xx * p.x + xz * p.z, z = xx * p.z - xz * p.x;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }
            if (points.Count == 0)
            {
                return box;
            }
            return box.Encapsulate(AvatarStage.ToCore(new Vector3(minX, minY, minZ)) * stage.Rig.TilesPerMeter)
                .Encapsulate(AvatarStage.ToCore(new Vector3(maxX, maxY, maxZ)) * stage.Rig.TilesPerMeter);
        }

        /// <summary>Every frame of every direction of an animation in one image: a row per direction.</summary>
        public Image8 Sheet(string animation, out double worstJointErrorPixels, Action<float> progress = null) =>
            Sheet(animation, out worstJointErrorPixels, progress, null);

        private Image8 Sheet(string animation, out double worstJointErrorPixels, Action<float> progress, GuideCheck guides)
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
            if (guides != null)
            {
                for (int row = 0; row < info.DirectionCount; row++)
                {
                    for (int frame = 0; frame < info.FrameCount; frame++)
                    {
                        DrawGuides(frames[row, frame], animation, row, frame, guides);
                    }
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
        public string SaveAllSheets(string folder, Action<string, float> progress = null, bool drawGuides = false,
            ICollection<string> only = null)
        {
            var clock = Stopwatch.StartNew();
            var report = new StringBuilder();
            Directory.CreateDirectory(folder);
            foreach (string note in Notes)
            {
                report.AppendLine(note);
            }
            string tuned = Tuning.Describe();
            if (tuned.Length > 0)
            {
                report.AppendLine("animation tuning: " + tuned);
            }
            List<AnimationInfo> all = AnimationSpec.All.Where(a => only == null || only.Contains(a.Name)).ToList();
            for (int i = 0; i < all.Count; i++)
            {
                AnimationInfo info = all[i];
                int index = i;
                GuideCheck guides = drawGuides ? new GuideCheck() : null;
                Image8 sheet = Sheet(info.Name, out double worst, p => progress?.Invoke(info.Name, (index + p) / all.Count), guides);
                File.WriteAllBytes(Path.Combine(folder, info.Name + ".png"), PngWriter.Encode(sheet));
                report.AppendLine($"{info.Name}: {info.DirectionCount} directions x {info.FrameCount} frames of " +
                    $"{sheet.Width / info.FrameCount}x{sheet.Height / info.DirectionCount} px; bones within {worst:0.0} px of Core's joints");
                if (guides != null)
                {
                    report.AppendLine("  " + guides.Describe(info.Name));
                }
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
            // the arm-flapping set goes with wing-flapping packs, for players to choose in the game; the other flight
            // methods already fly that way (or float)
            options.ArmFlapping = Tuning.Flight == FlightMethod.WingFlapping;
            using (var renderer = new StageRenderer(stage, camera))
            {
                string tuned = Tuning.Describe();
                IEnumerable<string> notes = Notes.Append(SizeNote).Append(camera.Lighting.Describe())
                    .Append(tuned.Length > 0 ? "animation tuning: " + tuned : "animation tuning: as designed");
                return PackExport.Write(options, AvatarName, poses, renderer, notes, progress, cancel);
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

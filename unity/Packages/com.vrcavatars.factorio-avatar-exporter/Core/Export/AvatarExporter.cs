using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static System.FormattableString;

namespace FactorioAvatarExporter.Core
{
    /// <summary>How the exporter renders.</summary>
    public sealed class ExportSettings
    {
        /// <summary>Pixels per tile of the sheets: 64 (sprites drawn at scale 0.5) or 32.</summary>
        public int PixelsPerTile { get; set; } = Conventions.DefaultPixelsPerTile;

        /// <summary>The renderer draws this many times the final resolution per axis (antialiasing).</summary>
        public int Supersampling { get; set; } = 2;

        /// <summary>Also render the flight animations (Space Age mech armor).</summary>
        public bool Flight { get; set; } = true;

        /// <summary>Largest sheet file, px; larger sheets are split by rows.</summary>
        public int MaxSheetSize { get; set; } = PackFormat.MaxSheetSize;

        /// <summary>
        /// Empty pixels kept around everything drawn when planning canvases. With at least 1, a render that
        /// reaches the edge of its canvas stops the export (the renderer's bounds were too small).
        /// </summary>
        public int CanvasMarginPixels { get; set; } = CanvasPlanner.DefaultMarginPixels;

        /// <summary>
        /// Draw an animation whose sheets would not fit at <see cref="PixelsPerTile"/> 64 at 32 instead (half the
        /// resolution, the same size in the game), rather than stopping the export
        /// (<see cref="AvatarExporter.CheckFit"/> says which ones will be).
        /// </summary>
        public bool HalveWhenTooLarge { get; set; }

        /// <summary>
        /// Let a render that reaches the edge of its canvas through, cut off there, instead of stopping the
        /// export; <see cref="AvatarExporter.CutOff"/> lists those frames.
        /// </summary>
        public bool AllowCutOff { get; set; }

        public void Validate()
        {
            if (PixelsPerTile != 64 && PixelsPerTile != 32)
            {
                throw new ArgumentException("pixels per tile must be 64 or 32, not " + PixelsPerTile);
            }
            if (Supersampling < 1 || Supersampling > 8)
            {
                throw new ArgumentException("supersampling must be between 1 and 8, not " + Supersampling);
            }
            if (MaxSheetSize < 1 || MaxSheetSize > PackFormat.MaxSheetSize)
            {
                throw new ArgumentException("the largest sheet must be between 1 and " + PackFormat.MaxSheetSize + " px");
            }
            if (CanvasMarginPixels < 0)
            {
                throw new ArgumentException("the canvas margin must not be negative");
            }
        }
    }

    /// <summary>Progress of an export, reported after every render.</summary>
    public readonly struct ExportProgress
    {
        public ExportProgress(string animation, int done, int total)
        {
            Animation = animation;
            Done = done;
            Total = total;
        }

        /// <summary>The animation being rendered.</summary>
        public string Animation { get; }

        /// <summary>Renders finished and planned in the whole export.</summary>
        public int Done { get; }
        public int Total { get; }

        public double Fraction => Total == 0 ? 1 : (double)Done / Total;
    }

    /// <summary>Everything rendered for a pack: sheets of every animation and layer, the icon and the thumbnail.</summary>
    public sealed class ExportedArt
    {
        public ExportedArt(Dictionary<string, Dictionary<string, SheetSet>> animations, Image8 icon, Image8 thumbnail,
            Dictionary<string, int> pixelsPerTile = null)
        {
            Animations = animations;
            Icon = icon;
            Thumbnail = thumbnail;
            PixelsPerTile = pixelsPerTile ?? new Dictionary<string, int>(StringComparer.Ordinal);
        }

        /// <summary>Pixels per tile each animation was drawn at (<see cref="ExportSettings.HalveWhenTooLarge"/>).</summary>
        public Dictionary<string, int> PixelsPerTile { get; }

        /// <summary>Animation name -> layer name (<see cref="PackLayers"/>) -> sheets.</summary>
        public Dictionary<string, Dictionary<string, SheetSet>> Animations { get; }
        public Image8 Icon { get; }
        public Image8 Thumbnail { get; }
    }

    /// <summary>Whether an animation fits in sprite sheets (<see cref="AvatarExporter.CheckFit"/>).</summary>
    public readonly struct AnimationFit
    {
        public AnimationFit(string animation, int pixelsPerTile, bool fits, int width, int height)
        {
            Animation = animation;
            PixelsPerTile = pixelsPerTile;
            Fits = fits;
            Width = width;
            Height = height;
        }

        public string Animation { get; }

        /// <summary>The pixels per tile it will be drawn at.</summary>
        public int PixelsPerTile { get; }

        /// <summary>False when it does not fit even at that resolution.</summary>
        public bool Fits { get; }

        /// <summary>The size of its largest canvas at that resolution, px (a frame is at most this).</summary>
        public int Width { get; }
        public int Height { get; }
    }

    /// <summary>
    /// Renders every frame of every animation of a pack and builds its sprite sheets: the part of the
    /// exporter that is the same whether frames come from Unity or from a software renderer.
    ///
    /// For each animation: pose every frame (<see cref="IPoseSource"/>), measure what each pose draws to
    /// plan one canvas per layer (<see cref="CanvasPlanner"/>), then for every frame render the character
    /// on black and on white (difference matting), and its ground shadow; for run-gun also the shadow of
    /// the mirrored character ("shadow-flipped", what Factorio draws while it mirrors the sprite for a gun
    /// aiming west, as tools/dragon_render/generate.py does). Each layer's frames become sheets
    /// (<see cref="SheetBuilder"/>).
    /// </summary>
    public sealed class AvatarExporter
    {
        /// <summary>The sheet row whose figure faces the viewer (south) in 8-direction animations.</summary>
        private const int SouthRow = 4;

        private readonly IPoseSource poses;
        private readonly IFrameRenderer renderer;
        private readonly ExportSettings settings;
        private readonly List<string> cutOff = new List<string>();

        /// <summary>The frames that reached the edge of their canvas and were cut off (<see cref="ExportSettings.AllowCutOff"/>).</summary>
        public IReadOnlyList<string> CutOff => cutOff;

        public AvatarExporter(IPoseSource poses, IFrameRenderer renderer, ExportSettings settings)
        {
            this.poses = poses ?? throw new ArgumentNullException(nameof(poses));
            this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            settings.Validate();
        }

        /// <summary>The animations this export renders, in order.</summary>
        public IEnumerable<AnimationInfo> Animations => AnimationSpec.ForPack(settings.Flight);

        /// <summary>Renders a frame takes in total (color, shadow and shadow-flipped passes).</summary>
        public int TotalRenders
        {
            get
            {
                int total = 0;
                foreach (AnimationInfo a in Animations)
                {
                    total += a.DirectionCount * a.FrameCount * PassesPerFrame(a.Name);
                }
                return total;
            }
        }

        /// <summary>
        /// Without rendering: poses and measures every frame of every animation, as the export does first, and
        /// says the resolution each will be drawn at and whether it fits. Measured bounds can fall short of what a
        /// renderer really draws (fur, outlines), so an animation close to the limit may still be halved or fail.
        /// </summary>
        public IReadOnlyList<AnimationFit> CheckFit(CancellationToken cancel = default)
        {
            var fits = new List<AnimationFit>();
            foreach (AnimationInfo animation in Animations)
            {
                cancel.ThrowIfCancellationRequested();
                var job = new AnimationJob(this, animation);
                job.Measure();
                fits.Add(job.Fit());
            }
            return fits;
        }

        /// <summary>
        /// The pixels per tile an animation is drawn at: <see cref="ExportSettings.PixelsPerTile"/>, or 32 when it
        /// does not fit at 64 but does at 32 and <see cref="ExportSettings.HalveWhenTooLarge"/> is on. Also whether
        /// it fits then, and its largest canvas.
        /// </summary>
        private AnimationFit Fit(AnimationInfo animation, List<Bounds3> color, List<Bounds3> mirrored)
        {
            (bool fits, int width, int height) At(int ppt)
            {
                var canvases = new List<CanvasSpec>();
                try
                {
                    canvases.Add(CanvasPlanner.PlanColor(color, ppt, settings.Supersampling, settings.CanvasMarginPixels));
                    canvases.Add(CanvasPlanner.PlanShadow(color, ppt, settings.Supersampling, settings.CanvasMarginPixels));
                    if (mirrored.Count > 0)
                    {
                        canvases.Add(CanvasPlanner.PlanShadow(mirrored, ppt, settings.Supersampling, settings.CanvasMarginPixels));
                    }
                }
                catch (ArgumentException)
                {
                    return (false, CanvasPlanner.MaxCanvasSize, CanvasPlanner.MaxCanvasSize);
                }
                int m = settings.MaxSheetSize;
                // a layer's sheets are split by rows or by frames, never both (SheetBuilder)
                bool all = canvases.All(c => c.Width <= m && c.Height <= m &&
                    (animation.FrameCount * c.Width <= m || animation.DirectionCount * c.Height <= m));
                return (all, canvases.Max(c => c.Width), canvases.Max(c => c.Height));
            }
            int preferred = settings.PixelsPerTile;
            (bool fits, int width, int height) full = At(preferred);
            if (!full.fits && settings.HalveWhenTooLarge && preferred == 64)
            {
                (bool fits, int width, int height) half = At(32);
                if (half.fits)
                {
                    return new AnimationFit(animation.Name, 32, true, half.width, half.height);
                }
            }
            return new AnimationFit(animation.Name, preferred, full.fits, full.width, full.height);
        }

        private static int PassesPerFrame(string animation) =>
            PackLayers.OptionalFor(animation).Contains(PackLayers.ShadowFlipped) ? 3 : 2;

        /// <summary>Renders everything. Throws <see cref="OperationCanceledException"/> when cancelled.</summary>
        public ExportedArt Render(IProgress<ExportProgress> progress = null, CancellationToken cancel = default)
        {
            var sheets = new Dictionary<string, Dictionary<string, SheetSet>>(StringComparer.Ordinal);
            var resolutions = new Dictionary<string, int>(StringComparer.Ordinal);
            int total = TotalRenders;
            int done = 0;
            Image8 portrait = null;
            int? iconBottom = null;
            foreach (AnimationInfo animation in Animations)
            {
                var job = new AnimationJob(this, animation);
                sheets[animation.Name] = job.Render(progress, cancel, ref done, total);
                resolutions[animation.Name] = job.PixelsPerTile;
                if (animation.Name == AnimationSpec.Idle)
                {
                    int row = Math.Min(SouthRow, animation.DirectionCount - 1);
                    portrait = job.BaseFrame(row, 0);
                    iconBottom = job.BelowShoulders(row, 0);
                }
            }
            if (portrait == null)
            {
                throw new InvalidOperationException("the idle animation was not rendered");
            }
            return new ExportedArt(sheets, PackArt.Icon(portrait, iconBottom), PackArt.Thumbnail(portrait), resolutions);
        }

        /// <summary>Copies rendered art and the pose source's timing into a pack.</summary>
        public void Fill(PackContent content, ExportedArt art)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            if (art == null)
            {
                throw new ArgumentNullException(nameof(art));
            }
            content.PixelsPerTile = settings.PixelsPerTile;
            content.Flight = settings.Flight;
            content.Timing = poses.Timing;
            // every animation's own speed, so the game plays it as long as it was posed (the mod's defaults are
            // for other frame counts)
            content.AnimationSpeeds.Clear();
            foreach (AnimationInfo animation in Animations)
            {
                content.AnimationSpeeds[animation.Name] = animation.DefaultSpeed;
            }
            content.Icon = art.Icon;
            content.Thumbnail = art.Thumbnail;
            content.Animations.Clear();
            content.AnimationPixelsPerTile.Clear();
            foreach (KeyValuePair<string, int> resolution in art.PixelsPerTile)
            {
                if (resolution.Value != settings.PixelsPerTile)
                {
                    content.AnimationPixelsPerTile[resolution.Key] = resolution.Value;
                }
            }
            foreach (KeyValuePair<string, Dictionary<string, SheetSet>> animation in art.Animations)
            {
                foreach (KeyValuePair<string, SheetSet> layer in animation.Value)
                {
                    content.SetLayer(animation.Key, layer.Key, layer.Value);
                }
            }
        }

        /// <summary>The renders of one animation.</summary>
        private sealed class AnimationJob
        {
            private readonly AvatarExporter owner;
            private readonly AnimationInfo animation;
            private readonly bool flippedShadow;
            private readonly PoseResult[,] framePoses;
            private readonly DirectionInfo[] directions;
            private CanvasSpec colorCanvas;
            private Image8[][] baseFrames;
            private List<Bounds3> colorBounds, mirroredBounds;

            /// <summary>The pixels per tile this animation is drawn at (set by <see cref="Render"/>).</summary>
            public int PixelsPerTile { get; private set; }

            public AnimationJob(AvatarExporter owner, AnimationInfo animation)
            {
                this.owner = owner;
                this.animation = animation;
                flippedShadow = PackLayers.OptionalFor(animation.Name).Contains(PackLayers.ShadowFlipped);
                directions = new DirectionInfo[animation.DirectionCount];
                framePoses = new PoseResult[animation.DirectionCount, animation.FrameCount];
                for (int d = 0; d < animation.DirectionCount; d++)
                {
                    directions[d] = AnimationSpec.Direction(animation.Name, d);
                    for (int f = 0; f < animation.FrameCount; f++)
                    {
                        double t = AnimationSpec.FramePhase(animation.Name, f);
                        framePoses[d, f] = owner.poses.Pose(animation.Name, t, directions[d].Move)
                            ?? throw new InvalidOperationException(
                                $"the pose source returned no pose for {animation.Name} frame {f + 1}");
                    }
                }
            }

            public Image8 BaseFrame(int direction, int frame) => baseFrames[direction][frame];

            /// <summary>
            /// The first pixel row of a base frame below the shoulders, where the icon ends
            /// (<see cref="PackArt.Icon"/>): the chest joint's position on the color canvas, or the lower
            /// shoulder joint's when the chest is not below it, rounded up to a whole row. Null when the pose
            /// lacks those joints.
            /// </summary>
            public int? BelowShoulders(int direction, int frame)
            {
                Dictionary<string, Vec3> joints = framePoses[direction, frame].Joints;
                if (!joints.TryGetValue(JointNames.Shoulder(-1), out Vec3 left)
                    || !joints.TryGetValue(JointNames.Shoulder(1), out Vec3 right)
                    || !joints.TryGetValue(JointNames.Chest, out Vec3 chest))
                {
                    return null;
                }
                double orientation = directions[direction].Orientation;
                double Row(Vec3 joint) =>
                    FactorioCamera.Project(Conventions.ModelPointToWorld(joint, orientation, false), colorCanvas).y;
                return (int)Math.Ceiling(Math.Max(Math.Max(Row(left), Row(right)), Row(chest)));
            }

            /// <summary>The orientation of the mirrored character whose shadow is "shadow-flipped".</summary>
            private static double MirroredOrientation(double orientation) => MathUtil.PositiveModulo(1.0 - orientation, 1.0);

            /// <summary>Measures what every frame draws, for planning its canvases.</summary>
            public void Measure()
            {
                colorBounds = new List<Bounds3>();
                mirroredBounds = new List<Bounds3>();
                for (int d = 0; d < animation.DirectionCount; d++)
                {
                    for (int f = 0; f < animation.FrameCount; f++)
                    {
                        colorBounds.Add(owner.renderer.MeasureWorldBounds(framePoses[d, f], directions[d].Orientation, false));
                        if (flippedShadow)
                        {
                            mirroredBounds.Add(owner.renderer.MeasureWorldBounds(framePoses[d, f],
                                MirroredOrientation(directions[d].Orientation), true));
                        }
                    }
                }
            }

            public AnimationFit Fit() => owner.Fit(animation, colorBounds, mirroredBounds);

            public Dictionary<string, SheetSet> Render(IProgress<ExportProgress> progress, CancellationToken cancel,
                ref int done, int total)
            {
                ExportSettings s = owner.settings;
                Measure();
                int ppt = PixelsPerTile = Fit().PixelsPerTile;
                colorCanvas = CanvasPlanner.PlanColor(colorBounds, ppt, s.Supersampling, s.CanvasMarginPixels);
                CanvasSpec shadowCanvas = CanvasPlanner.PlanShadow(colorBounds, ppt, s.Supersampling, s.CanvasMarginPixels);
                CanvasSpec flippedCanvas = flippedShadow
                    ? CanvasPlanner.PlanShadow(mirroredBounds, ppt, s.Supersampling, s.CanvasMarginPixels)
                    : default;

                baseFrames = NewFrames();
                Image8[][] shadowFrames = NewFrames();
                Image8[][] flippedFrames = flippedShadow ? NewFrames() : null;
                for (int d = 0; d < animation.DirectionCount; d++)
                {
                    for (int f = 0; f < animation.FrameCount; f++)
                    {
                        cancel.ThrowIfCancellationRequested();
                        double orientation = directions[d].Orientation;
                        baseFrames[d][f] = RenderColor(d, f, orientation, colorCanvas);
                        Report(progress, ref done, total);
                        shadowFrames[d][f] = RenderShadow(d, f, orientation, false, shadowCanvas);
                        Report(progress, ref done, total);
                        if (flippedShadow)
                        {
                            flippedFrames[d][f] = RenderShadow(d, f, MirroredOrientation(orientation), true, flippedCanvas);
                            Report(progress, ref done, total);
                        }
                    }
                }

                var layers = new Dictionary<string, SheetSet>(StringComparer.Ordinal)
                {
                    [PackLayers.Base] = Sheets(baseFrames, colorCanvas, PackLayers.Base),
                    [PackLayers.Shadow] = Sheets(shadowFrames, shadowCanvas, PackLayers.Shadow),
                };
                if (flippedShadow)
                {
                    layers[PackLayers.ShadowFlipped] = Sheets(flippedFrames, flippedCanvas, PackLayers.ShadowFlipped);
                }
                return layers;
            }

            private void Report(IProgress<ExportProgress> progress, ref int done, int total)
            {
                done++;
                progress?.Report(new ExportProgress(animation.Name, done, total));
            }

            private Image8[][] NewFrames()
            {
                var frames = new Image8[animation.DirectionCount][];
                for (int d = 0; d < frames.Length; d++)
                {
                    frames[d] = new Image8[animation.FrameCount];
                }
                return frames;
            }

            private FrameRequest Request(int direction, int frame, double orientation, bool mirrored, RenderPass pass,
                CanvasSpec canvas) => new FrameRequest
                {
                    Animation = animation.Name,
                    Direction = direction,
                    Frame = frame,
                    Pose = framePoses[direction, frame],
                    Orientation = orientation,
                    Mirrored = mirrored,
                    Pass = pass,
                    Canvas = canvas,
                };

            private Image8 RenderColor(int direction, int frame, double orientation, CanvasSpec canvas)
            {
                RenderResult result = owner.renderer.Render(Request(direction, frame, orientation, false, RenderPass.Color, canvas));
                string what = Describe(direction, frame, "color pass");
                CheckSize(result?.OnBlack, canvas, what + " on black");
                CheckSize(result.OnWhite, canvas, what + " on white");
                ImageF premultiplied = Matting.FromBlackAndWhite(result.OnBlack, result.OnWhite, result.Linear);
                Image8 image = ImageConvert.ToImage8(Downsample.Box(premultiplied, canvas.Supersampling));
                CheckInsideCanvas(image, what);
                return image;
            }

            private Image8 RenderShadow(int direction, int frame, double orientation, bool mirrored, CanvasSpec canvas)
            {
                RenderResult result = owner.renderer.Render(Request(direction, frame, orientation, mirrored, RenderPass.Shadow, canvas));
                string what = Describe(direction, frame, mirrored ? "flipped shadow pass" : "shadow pass");
                CheckSize(result?.Silhouette, canvas, what);
                Image8 image = ImageConvert.ShadowFromSilhouette(result.Silhouette, canvas.Supersampling, result.Linear);
                CheckInsideCanvas(image, what);
                return image;
            }

            private string Describe(int direction, int frame, string pass) =>
                $"{animation.Name} direction {direction + 1} frame {frame + 1} {pass}";

            private static void CheckSize(ImageF image, CanvasSpec canvas, string what)
            {
                if (image == null)
                {
                    throw new InvalidOperationException("the renderer returned no image for " + what);
                }
                if (image.Width != canvas.RenderWidth || image.Height != canvas.RenderHeight)
                {
                    throw new InvalidOperationException(
                        $"the renderer returned a {image.Width}x{image.Height} image for {what}; expected {canvas.RenderWidth}x{canvas.RenderHeight}");
                }
            }

            /// <summary>
            /// Throws when a pixel on the outermost ring of a frame is drawn (alpha &gt; 0). The canvas keeps
            /// <see cref="ExportSettings.CanvasMarginPixels"/> empty pixels around the bounds the renderer
            /// measured, so a drawn edge pixel means the renderer drew outside them and the frame is cut off.
            /// Not checked without a margin, where drawing up to the edge is expected.
            /// </summary>
            private void CheckInsideCanvas(Image8 image, string what)
            {
                if (owner.settings.CanvasMarginPixels < 1)
                {
                    return;
                }
                if (owner.settings.AllowCutOff)
                {
                    if (TouchesEdge(image))
                    {
                        owner.cutOff.Add(what);
                    }
                    return;
                }
                for (int x = 0; x < image.Width; x++)
                {
                    CheckEmpty(image, x, 0, what);
                    CheckEmpty(image, x, image.Height - 1, what);
                }
                for (int y = 1; y < image.Height - 1; y++)
                {
                    CheckEmpty(image, 0, y, what);
                    CheckEmpty(image, image.Width - 1, y, what);
                }
            }

            private static bool TouchesEdge(Image8 image)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (image.Alpha(x, 0) != 0 || image.Alpha(x, image.Height - 1) != 0)
                    {
                        return true;
                    }
                }
                for (int y = 0; y < image.Height; y++)
                {
                    if (image.Alpha(0, y) != 0 || image.Alpha(image.Width - 1, y) != 0)
                    {
                        return true;
                    }
                }
                return false;
            }

            private static void CheckEmpty(Image8 image, int x, int y, string what)
            {
                if (image.Alpha(x, y) != 0)
                {
                    throw new InvalidOperationException(Invariant(
                        $"the renderer drew outside the bounds MeasureWorldBounds reported: {what} has a drawn pixel at {x},{y} ") +
                        "on the canvas edge, so the frame would be cut off. The bounds must cover everything drawn, outlines, " +
                        "hair and props included; a larger CanvasMarginPixels works around it.");
                }
            }

            private SheetSet Sheets(Image8[][] frames, CanvasSpec canvas, string layer)
            {
                ExportSettings s = owner.settings;
                try
                {
                    return SheetBuilder.Build(frames, canvas.AnchorX, canvas.AnchorY, canvas.PixelsPerTile, s.MaxSheetSize);
                }
                catch (InvalidOperationException e)
                {
                    // SheetBuilder knows neither the animation nor the settings that would make the frames fit
                    string fixes = (canvas.PixelsPerTile == 64 ? "export at 32 pixels per tile, " : "")
                        + (s.MaxSheetSize < PackFormat.MaxSheetSize ? Invariant($"raise MaxSheetSize (at most {PackFormat.MaxSheetSize}), ") : "");
                    throw new InvalidOperationException(Invariant(
                        $"the {animation.Name} {layer} frames do not fit in {s.MaxSheetSize} px sprite sheets: {animation.DirectionCount} directions of ") +
                        Invariant($"{animation.FrameCount} frames fit only when each frame is at most {s.MaxSheetSize / animation.FrameCount} px wide ") +
                        Invariant($"or {s.MaxSheetSize / animation.DirectionCount} px high (and {s.MaxSheetSize} px each way). ") +
                        $"To make them fit, {fixes}{(fixes.Length > 0 ? "or " : "")}keep the character from reaching out so far.", e);
                }
            }
        }
    }
}

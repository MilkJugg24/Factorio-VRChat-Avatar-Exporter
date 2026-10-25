using System;

namespace FactorioAvatarExporter.Core
{
    public enum RenderPass
    {
        /// <summary>
        /// The character itself: rendered twice, on black and on white, for difference matting
        /// (<see cref="Matting"/>), preferably into a half or float target (see <see cref="RenderResult"/>).
        /// </summary>
        Color,

        /// <summary>
        /// The ground shadow: the character's planar projection onto the ground along
        /// <see cref="Conventions.ShadowLight"/>, drawn as an opaque black silhouette on white.
        /// </summary>
        Shadow,
    }

    /// <summary>
    /// The pixel canvas of one layer of an animation. Sizes and the anchor are in final pixels; the
    /// renderer draws at <see cref="Supersampling"/> times that resolution.
    ///
    /// Projection (tools/dragon_render/render.py and sdf.py): a world point P lands at final pixel
    /// <c>x = AnchorX + PixelsPerTile * dot(P, camRight)</c>,
    /// <c>y = AnchorY - PixelsPerTile * VerticalStretch * dot(P, camUp)</c>, where the pixel (i, j)
    /// covers [i, i+1) x [j, j+1). The anchor is where the entity position (the world origin) lands.
    /// </summary>
    public readonly struct CanvasSpec
    {
        public CanvasSpec(int width, int height, int anchorX, int anchorY, int pixelsPerTile, int supersampling)
        {
            if (width <= 0 || height <= 0 || pixelsPerTile <= 0 || supersampling <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "canvas sizes must be positive");
            }
            Width = width;
            Height = height;
            AnchorX = anchorX;
            AnchorY = anchorY;
            PixelsPerTile = pixelsPerTile;
            Supersampling = supersampling;
        }

        public int Width { get; }
        public int Height { get; }
        public int AnchorX { get; }
        public int AnchorY { get; }
        public int PixelsPerTile { get; }
        public int Supersampling { get; }

        /// <summary>Size of the images the renderer returns.</summary>
        public int RenderWidth => Width * Supersampling;
        public int RenderHeight => Height * Supersampling;
    }

    /// <summary>One render the exporter asks for.</summary>
    public sealed class FrameRequest
    {
        public string Animation { get; set; }
        public int Direction { get; set; }
        public int Frame { get; set; }

        /// <summary>The pose, in model space.</summary>
        public PoseResult Pose { get; set; }

        /// <summary>Facing, as a fraction of a clockwise turn from north (<see cref="Conventions.ModelToWorld"/>).</summary>
        public double Orientation { get; set; }

        /// <summary>
        /// Mirror the posed model left/right (model x -> -x) before placing it: used for the
        /// "shadow-flipped" layer, the shadow of the sprite Factorio draws mirrored for guns aiming west.
        /// </summary>
        public bool Mirrored { get; set; }

        public RenderPass Pass { get; set; }

        public CanvasSpec Canvas { get; set; }
    }

    /// <summary>
    /// What a renderer returns for a <see cref="FrameRequest"/>.
    ///
    /// The color pass should render into an ARGBHalf or ARGBFloat target with HDR on and no
    /// post-processing, read back as linear floats (<see cref="Linear"/> = true): only values above 1 in
    /// the render on white let difference matting drop the light shaders add over the background (additive
    /// glow, particles; see <see cref="Matting"/>). sRGB values (<see cref="Linear"/> = false) and 8-bit
    /// targets are accepted, but an 8-bit target clamps at 1 and turns that light into colored, half
    /// transparent fog, so with one the Unity layer must hide additive and particle renderers.
    /// </summary>
    public sealed class RenderResult
    {
        /// <summary>Color pass: the frame rendered over opaque black. Size = canvas render size.</summary>
        public ImageF OnBlack { get; set; }

        /// <summary>Color pass: the same frame rendered over opaque white.</summary>
        public ImageF OnWhite { get; set; }

        /// <summary>Shadow pass: black silhouette on white (alpha ignored).</summary>
        public ImageF Silhouette { get; set; }

        /// <summary>
        /// True when the values of the images (color and shadow pass alike) are linear, as in a float render
        /// target in Unity's linear color space; false when they are sRGB encoded. It says how values are
        /// encoded, not whether they were clamped: an 8-bit linear target clamps like an sRGB one.
        /// </summary>
        public bool Linear { get; set; }
    }

    /// <summary>
    /// Draws poses. The Unity layer implements this with the real avatar; tests and the pack harness use
    /// a software stick figure.
    /// </summary>
    public interface IFrameRenderer
    {
        /// <summary>
        /// World space bounds (tiles) of everything the color pass would draw for this pose, placed with
        /// <paramref name="orientation"/> and <paramref name="mirrored"/>. Used to size canvases before
        /// rendering; it may be generous, but must not be too small (the exporter throws when a render
        /// reaches the edge of a canvas with a margin).
        /// </summary>
        Bounds3 MeasureWorldBounds(PoseResult pose, double orientation, bool mirrored);

        RenderResult Render(FrameRequest request);
    }
}

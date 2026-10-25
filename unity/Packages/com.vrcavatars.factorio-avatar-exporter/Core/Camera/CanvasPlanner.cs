using System;
using System.Collections.Generic;
using System.Globalization;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// Chooses canvases that hold everything a renderer draws. The planned canvas is the smallest one, with
    /// the anchor on a whole pixel, that puts every projected corner of the given world bounds inside
    /// pixels <c>margin .. size - margin - 1</c> on both axes; the anchor may lie outside the canvas. The
    /// result depends only on the set of corners, not on their order.
    /// </summary>
    public static class CanvasPlanner
    {
        /// <summary>Empty final pixels kept between the projected bounds and each canvas edge.</summary>
        public const int DefaultMarginPixels = 4;

        /// <summary>
        /// Largest canvas width or height, and largest distance of a corner from the anchor, in final pixels.
        /// Larger bounds are a mistake (256 tiles at 64 pixels per tile).
        /// </summary>
        public const int MaxCanvasSize = 16384;

        private const string WorldBoundsParameter = "worldBounds";

        /// <summary>
        /// The canvas of a color layer: every corner of <paramref name="worldBounds"/> (world space, tiles;
        /// empty boxes are skipped) as the camera sees it (<see cref="FactorioCamera.Project"/>).
        /// </summary>
        /// <param name="worldBounds">World space boxes, tiles, typically <see cref="IFrameRenderer.MeasureWorldBounds"/> of every frame of a layer.</param>
        /// <param name="pixelsPerTile">Final pixels per tile.</param>
        /// <param name="supersampling">Copied into the result; it does not change the plan.</param>
        /// <param name="marginPixels">Empty final pixels to keep at each edge.</param>
        /// <exception cref="ArgumentNullException"><paramref name="worldBounds"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A size is not positive or the margin is negative or huge.</exception>
        /// <exception cref="ArgumentException">No bounds are non-empty, a bound is not finite, or the canvas would be too large.</exception>
        public static CanvasSpec PlanColor(IEnumerable<Bounds3> worldBounds, int pixelsPerTile, int supersampling,
            int marginPixels = DefaultMarginPixels) =>
            Plan(worldBounds, false, pixelsPerTile, supersampling, marginPixels);

        /// <summary>
        /// The canvas of a shadow layer: the ground shadow (<see cref="PlanarShadow.Project"/>) of every corner
        /// of <paramref name="worldBounds"/> as the camera sees it. The shadow of a box is the hull of its
        /// corners' shadows, so the whole shadow fits.
        /// </summary>
        /// <param name="worldBounds">World space boxes, tiles, typically <see cref="IFrameRenderer.MeasureWorldBounds"/> of every frame of a layer.</param>
        /// <param name="pixelsPerTile">Final pixels per tile.</param>
        /// <param name="supersampling">Copied into the result; it does not change the plan.</param>
        /// <param name="marginPixels">Empty final pixels to keep at each edge.</param>
        /// <exception cref="ArgumentNullException"><paramref name="worldBounds"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A size is not positive or the margin is negative or huge.</exception>
        /// <exception cref="ArgumentException">No bounds are non-empty, a bound is not finite, or the canvas would be too large.</exception>
        public static CanvasSpec PlanShadow(IEnumerable<Bounds3> worldBounds, int pixelsPerTile, int supersampling,
            int marginPixels = DefaultMarginPixels) =>
            Plan(worldBounds, true, pixelsPerTile, supersampling, marginPixels);

        private static CanvasSpec Plan(IEnumerable<Bounds3> worldBounds, bool shadow, int pixelsPerTile,
            int supersampling, int marginPixels)
        {
            if (worldBounds == null)
            {
                throw new ArgumentNullException(nameof(worldBounds));
            }
            if (pixelsPerTile <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pixelsPerTile), pixelsPerTile, "pixels per tile must be positive");
            }
            if (supersampling <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(supersampling), supersampling, "supersampling must be positive");
            }
            if (marginPixels < 0 || marginPixels > MaxCanvasSize)
            {
                throw new ArgumentOutOfRangeException(nameof(marginPixels), marginPixels,
                    "the margin must be between 0 and " + MaxCanvasSize);
            }

            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
            foreach (Bounds3 bounds in worldBounds)
            {
                foreach (Vec3 corner in bounds.Corners())
                {
                    if (!VecChecks.IsFinite(corner))
                    {
                        throw new ArgumentException("world bounds must be finite, got a corner at " + corner,
                            nameof(worldBounds));
                    }
                    (double x, double y) = FactorioCamera.OffsetFromAnchor(shadow ? PlanarShadow.Project(corner) : corner,
                        pixelsPerTile);
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
            if (minX > maxX)
            {
                throw new ArgumentException("there are no non-empty bounds to plan a canvas for", nameof(worldBounds));
            }

            (int anchorX, int width) = Span(minX, maxX, marginPixels, "wide");
            (int anchorY, int height) = Span(minY, maxY, marginPixels, "high");
            return new CanvasSpec(width, height, anchorX, anchorY, pixelsPerTile, supersampling);
        }

        /// <summary>
        /// Anchor and size along one axis so that offsets from the anchor in [min, max] land in pixels
        /// margin .. size - margin - 1: anchor + min >= margin and anchor + max &lt; size - margin.
        /// </summary>
        private static (int anchor, int size) Span(double min, double max, int margin, string sizeWord)
        {
            if (Math.Max(Math.Abs(min), Math.Abs(max)) > MaxCanvasSize)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                    "the bounds reach {0:0.#} pixels from the anchor; at most {1} is allowed",
                    Math.Max(Math.Abs(min), Math.Abs(max)), MaxCanvasSize), WorldBoundsParameter);
            }
            double anchor = margin + Math.Ceiling(-min);
            double size = anchor + Math.Floor(max) + 1 + margin;
            if (size > MaxCanvasSize)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                    "the canvas would be {0} pixels {1}; at most {2} is allowed", size, sizeWord, MaxCanvasSize),
                    WorldBoundsParameter);
            }
            return ((int)anchor, (int)size);
        }
    }
}

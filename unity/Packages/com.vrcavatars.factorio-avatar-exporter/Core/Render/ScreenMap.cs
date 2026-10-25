using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// An affine map from world space (tiles) to the pixel coordinates of a render target at the
    /// supersampled resolution: <c>x = OffsetX + dot(RowX, P)</c>, <c>y = OffsetY + dot(RowY, P)</c>. Every
    /// point of a view ray (or of a shadow ray, for <see cref="GroundShadow"/>) maps to the centre of the
    /// pixel the ray belongs to, which is what lets <see cref="CapsuleRows"/> cull capsules exactly.
    /// </summary>
    internal readonly struct ScreenMap
    {
        private readonly Vec3 rowX;
        private readonly Vec3 rowY;
        private readonly double offsetX;
        private readonly double offsetY;

        private ScreenMap(Vec3 rowX, double offsetX, Vec3 rowY, double offsetY)
        {
            this.rowX = rowX;
            this.offsetX = offsetX;
            this.rowY = rowY;
            this.offsetY = offsetY;
        }

        /// <summary>Where the camera shows a world point: <see cref="FactorioCamera.Project"/> times the supersampling.</summary>
        public static ScreenMap Camera(CanvasSpec canvas)
        {
            double scale = (double)canvas.Supersampling * canvas.PixelsPerTile;
            return new ScreenMap(FactorioCamera.Right * scale, (double)canvas.Supersampling * canvas.AnchorX,
                FactorioCamera.Up * (-scale * Conventions.VerticalStretch), (double)canvas.Supersampling * canvas.AnchorY);
        }

        /// <summary>
        /// Where the camera shows the ground shadow (<see cref="PlanarShadow.Project"/>) of a world point.
        /// The shadow projection is linear, so this is <see cref="Camera"/> with each row pulled back through it.
        /// </summary>
        public static ScreenMap GroundShadow(CanvasSpec canvas)
        {
            ScreenMap camera = Camera(canvas);
            return new ScreenMap(ThroughShadow(camera.rowX), camera.offsetX, ThroughShadow(camera.rowY), camera.offsetY);
        }

        /// <summary>
        /// The pixel coordinate box holding the image of every point of <paramref name="capsule"/>: the box of
        /// its end points widened by the radius times the length of each row (the extent of a sphere's image).
        /// </summary>
        public (double minX, double maxX, double minY, double maxY) Box(Capsule capsule)
        {
            (double minX, double maxX) = Range(rowX, offsetX, capsule);
            (double minY, double maxY) = Range(rowY, offsetY, capsule);
            return (minX, maxX, minY, maxY);
        }

        private static (double min, double max) Range(Vec3 row, double offset, Capsule capsule)
        {
            double a = offset + Vec3.Dot(row, capsule.A);
            double b = offset + Vec3.Dot(row, capsule.B);
            double reach = capsule.Radius * row.Length;
            return (Math.Min(a, b) - reach, Math.Max(a, b) + reach);
        }

        // dot(row, S(P)) = dot(S^T row, P) for the linear shadow projection S
        private static Vec3 ThroughShadow(Vec3 row) => new Vec3(
            Vec3.Dot(row, PlanarShadow.Project(Vec3.UnitX)),
            Vec3.Dot(row, PlanarShadow.Project(Vec3.UnitY)),
            Vec3.Dot(row, PlanarShadow.Project(Vec3.UnitZ)));
    }
}

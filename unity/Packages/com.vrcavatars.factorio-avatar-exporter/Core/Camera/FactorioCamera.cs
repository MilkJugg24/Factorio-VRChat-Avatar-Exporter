using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// The sprite camera of tools/dragon_render (render.py <c>camera()</c>, sdf.py <c>render</c>):
    /// orthographic, looking north and <see cref="Conventions.CameraElevation"/> down, with the screen's
    /// vertical stretched by <see cref="Conventions.VerticalStretch"/>.
    ///
    /// Axes are world space (x east, y north, z up) and lengths are tiles. Pixel positions are in final
    /// pixels of a <see cref="CanvasSpec"/>, y growing downwards; pixel (i, j) covers [i, i+1) x [j, j+1),
    /// so its centre is (i + 0.5, j + 0.5). A renderer working at the supersampled resolution looks at
    /// its pixel (i, j) through final pixel position ((i + 0.5) / s, (j + 0.5) / s) for supersampling s.
    /// </summary>
    public static class FactorioCamera
    {
        /// <summary>Screen right: east, (1, 0, 0).</summary>
        public static readonly Vec3 Right = Vec3.UnitX;

        /// <summary>
        /// Screen up: (0, sin e, cos e) for the elevation e. render.py's <c>U</c> with y turned north.
        /// </summary>
        public static readonly Vec3 Up =
            new Vec3(0, Math.Sin(Conventions.CameraElevation), Math.Cos(Conventions.CameraElevation));

        /// <summary>
        /// The view direction, north and down: (0, cos e, -sin e). render.py's <c>D</c> with y turned north.
        /// Right, Forward and Up form a right-handed orthonormal basis.
        /// </summary>
        public static readonly Vec3 Forward =
            new Vec3(0, Math.Cos(Conventions.CameraElevation), -Math.Sin(Conventions.CameraElevation));

        /// <summary>
        /// How far the camera sits behind the camera plane (the plane through the world origin facing the
        /// camera, <c>dot(P, Forward) = 0</c>), tiles. View rays start this far back (<see cref="ViewRayOrigin"/>).
        /// </summary>
        public const double BackOffTiles = 20.0;

        /// <summary>
        /// Depth of the slab the camera sees, tiles: from <see cref="BackOffTiles"/> in front of the camera
        /// plane to as far behind it.
        /// </summary>
        public const double DepthTiles = 2 * BackOffTiles;

        /// <summary>
        /// Where a world point lands relative to the anchor, in final pixels:
        /// <c>(ppt * dot(P, Right), -ppt * VerticalStretch * dot(P, Up))</c>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="pixelsPerTile"/> is not positive.</exception>
        public static (double x, double y) OffsetFromAnchor(Vec3 world, int pixelsPerTile)
        {
            if (pixelsPerTile <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pixelsPerTile), pixelsPerTile,
                    "pixels per tile must be positive");
            }
            return (pixelsPerTile * Vec3.Dot(world, Right),
                -pixelsPerTile * Conventions.VerticalStretch * Vec3.Dot(world, Up));
        }

        /// <summary>
        /// Where a world point lands on <paramref name="canvas"/>, in final pixels (see <see cref="CanvasSpec"/>):
        /// <c>x = AnchorX + ppt * dot(P, Right)</c>, <c>y = AnchorY - ppt * VerticalStretch * dot(P, Up)</c>.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="canvas"/> is <c>default</c>.</exception>
        public static (double x, double y) Project(Vec3 world, CanvasSpec canvas)
        {
            RequireCanvas(canvas);
            (double dx, double dy) = OffsetFromAnchor(world, canvas.PixelsPerTile);
            return (canvas.AnchorX + dx, canvas.AnchorY + dy);
        }

        /// <summary>
        /// The point of the camera plane (through the world origin, <c>dot(P, Forward) = 0</c>) that lands at
        /// final pixel position (<paramref name="x"/>, <paramref name="y"/>): <see cref="Project"/> inverted.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="canvas"/> is <c>default</c>.</exception>
        public static Vec3 Unproject(double x, double y, CanvasSpec canvas)
        {
            RequireCanvas(canvas);
            double right = (x - canvas.AnchorX) / canvas.PixelsPerTile;
            double up = (canvas.AnchorY - y) / (canvas.PixelsPerTile * Conventions.VerticalStretch);
            return Right * right + Up * up;
        }

        /// <summary>
        /// Start of the view ray through final pixel position (<paramref name="x"/>, <paramref name="y"/>):
        /// <see cref="BackOffTiles"/> in front of the camera plane, on the near plane of <see cref="Setup"/>'s
        /// camera. The ray runs along <see cref="Forward"/> and leaves the camera's view after
        /// <see cref="DepthTiles"/>.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="canvas"/> is <c>default</c>.</exception>
        public static Vec3 ViewRayOrigin(double x, double y, CanvasSpec canvas) =>
            Unproject(x, y, canvas) - Forward * BackOffTiles;

        /// <summary>
        /// The ground point (z = 0) seen at final pixel position (<paramref name="x"/>, <paramref name="y"/>),
        /// as sdf.py's <c>render_ground_shadow</c> finds it.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="canvas"/> is <c>default</c>.</exception>
        public static Vec3 GroundPoint(double x, double y, CanvasSpec canvas)
        {
            Vec3 p = Unproject(x, y, canvas);
            return (p - Forward * (p.Z / Forward.Z)).WithZ(0);
        }

        /// <summary>
        /// The orthographic camera that sees exactly <paramref name="canvas"/>: placed
        /// <see cref="BackOffTiles"/> in front of the point of the camera plane at the canvas centre, turned
        /// so its x, y and z axes are <see cref="Right"/>, <see cref="Forward"/> and <see cref="Up"/>, with
        /// half extents <c>Width / (2 ppt)</c> and <c>Height / (2 ppt VerticalStretch)</c> tiles and a
        /// <see cref="DepthTiles"/> deep view from near = 0, so it sees everything within
        /// <see cref="BackOffTiles"/> of the camera plane.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="canvas"/> is <c>default</c>.</exception>
        public static CameraSetup Setup(CanvasSpec canvas)
        {
            RequireCanvas(canvas);
            Vec3 centre = Unproject(canvas.Width * 0.5, canvas.Height * 0.5, canvas);
            double halfWidth = canvas.Width / (2.0 * canvas.PixelsPerTile);
            double halfHeight = canvas.Height / (2.0 * canvas.PixelsPerTile * Conventions.VerticalStretch);
            return new CameraSetup(centre - Forward * BackOffTiles, Quat.FromBasis(Right, Forward, Up),
                halfWidth, halfHeight, 0.0, DepthTiles, canvas.RenderWidth, canvas.RenderHeight);
        }

        /// <summary>
        /// Rejects <c>default(CanvasSpec)</c>, which skips the constructor's checks and has no sizes.
        /// </summary>
        internal static void RequireCanvas(CanvasSpec canvas)
        {
            if (canvas.PixelsPerTile <= 0 || canvas.Supersampling <= 0 || canvas.Width <= 0 || canvas.Height <= 0)
            {
                throw new ArgumentException("the canvas is not set (use the CanvasSpec constructor)", nameof(canvas));
            }
        }
    }
}

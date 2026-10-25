using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// An orthographic camera that sees exactly one canvas (<see cref="FactorioCamera.Setup"/>), in Core
    /// world space and tiles.
    ///
    /// A world point P has view coordinates <c>v = Rotation.Inverse.Rotate(P - Position)</c>. It lands at
    /// final pixel <c>x = canvas.Width / 2 * (1 + v.X / HalfWidth)</c>,
    /// <c>y = canvas.Height / 2 * (1 - v.Z / HalfHeight)</c> of the canvas the camera was set up for, the same
    /// place as <see cref="FactorioCamera.Project"/>, and is drawn when its depth v.Y lies in
    /// [<see cref="Near"/>, <see cref="Far"/>].
    /// </summary>
    public sealed class CameraSetup
    {
        internal CameraSetup(Vec3 position, Quat rotation, double halfWidth, double halfHeight, double near,
            double far, int targetWidth, int targetHeight)
        {
            Position = position;
            Rotation = rotation;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
            Near = near;
            Far = far;
            TargetWidth = targetWidth;
            TargetHeight = targetHeight;
        }

        /// <summary>World position of the camera, tiles.</summary>
        public Vec3 Position { get; }

        /// <summary>
        /// World rotation of the camera: maps x to <see cref="FactorioCamera.Right"/>, y to
        /// <see cref="FactorioCamera.Forward"/> (the view direction) and z to <see cref="FactorioCamera.Up"/>.
        /// </summary>
        public Quat Rotation { get; }

        /// <summary>Half the width of the view, tiles.</summary>
        public double HalfWidth { get; }

        /// <summary>Half the height of the view along <see cref="FactorioCamera.Up"/>, tiles (the stretch is in here).</summary>
        public double HalfHeight { get; }

        /// <summary>Distance from the camera to the near plane along the view direction, tiles.</summary>
        public double Near { get; }

        /// <summary>Distance from the camera to the far plane along the view direction, tiles.</summary>
        public double Far { get; }

        /// <summary>Width of the image to render, pixels: the canvas width times its supersampling.</summary>
        public int TargetWidth { get; }

        /// <summary>Height of the image to render, pixels: the canvas height times its supersampling.</summary>
        public int TargetHeight { get; }

        /// <summary>
        /// This camera in Unity space (<see cref="UnitySpace"/>) for a scene scaled
        /// <paramref name="metersPerTile"/> meters to the tile, with the world origin (the entity position)
        /// at Unity's origin.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="metersPerTile"/> is not a positive number.</exception>
        public UnityCameraSetup ToUnity(double metersPerTile)
        {
            if (!(metersPerTile > 0) || !VecChecks.IsFinite(metersPerTile))
            {
                throw new ArgumentOutOfRangeException(nameof(metersPerTile), metersPerTile,
                    "meters per tile must be a positive number");
            }
            return new UnityCameraSetup(UnitySpace.ToUnity(Position * metersPerTile), UnitySpace.RotationToUnity(Rotation),
                HalfWidth * metersPerTile, HalfHeight * metersPerTile, Near * metersPerTile, Far * metersPerTile,
                TargetWidth, TargetHeight);
        }
    }

    /// <summary>
    /// A <see cref="CameraSetup"/> in Unity space, lengths in meters. The Unity layer renders a
    /// <see cref="TargetWidth"/> x <see cref="TargetHeight"/> target with a camera at <see cref="Position"/>
    /// turned by <see cref="Rotation"/> (it then looks along the camera's +z, which is
    /// <see cref="FactorioCamera.Forward"/>, with its +y along <see cref="FactorioCamera.Up"/>) and
    /// <c>projectionMatrix = Matrix4x4.Ortho(-HalfWidth, HalfWidth, -HalfHeight, HalfHeight, Near, Far)</c>.
    /// </summary>
    public sealed class UnityCameraSetup
    {
        internal UnityCameraSetup((double x, double y, double z) position, (double x, double y, double z, double w) rotation,
            double halfWidth, double halfHeight, double near, double far, int targetWidth, int targetHeight)
        {
            Position = position;
            Rotation = rotation;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
            Near = near;
            Far = far;
            TargetWidth = targetWidth;
            TargetHeight = targetHeight;
        }

        /// <summary>Camera position in Unity's axes, meters.</summary>
        public (double x, double y, double z) Position { get; }

        /// <summary>Camera rotation as a Unity quaternion (x, y, z, w).</summary>
        public (double x, double y, double z, double w) Rotation { get; }

        /// <summary>Half the width of the orthographic view, meters.</summary>
        public double HalfWidth { get; }

        /// <summary>Half the height of the orthographic view, meters.</summary>
        public double HalfHeight { get; }

        /// <summary>Distance from the camera to the near plane, meters.</summary>
        public double Near { get; }

        /// <summary>Distance from the camera to the far plane, meters.</summary>
        public double Far { get; }

        /// <summary>Width of the render target, pixels.</summary>
        public int TargetWidth { get; }

        /// <summary>Height of the render target, pixels.</summary>
        public int TargetHeight { get; }
    }
}

using System;

namespace MilkJugg24.FactorioAvatarExporter.Core
{
    /// <summary>
    /// How sprites are rendered so packs match the dragon (docs/avatar-pack-format.md,
    /// "Rendering conventions"; values from tools/dragon_render/render.py).
    ///
    /// Spaces used throughout Core (all right-handed, z up, units are tiles unless a name says
    /// otherwise):
    /// <list type="bullet">
    /// <item>Model space: the character's own space. x = its right, y = its forward, z = up; the
    /// ground point under the character (the entity position) is the origin. Same as model.py.</item>
    /// <item>World space: x = east, y = north, z = up; the entity position is the origin. A model is
    /// placed in the world by turning it clockwise (seen from above) by its orientation (0 = facing
    /// north, 0.25 = east), see <see cref="ModelToWorld"/>. (The Python renderer stores world y as
    /// south; the geometry is the same.)</item>
    /// <item>Unity space: x = east/right, y = up, z = north/forward, left-handed, meters. See
    /// <see cref="UnitySpace"/>.</item>
    /// </list>
    /// </summary>
    public static class Conventions
    {
        public const int DefaultPixelsPerTile = 64;

        /// <summary>Camera elevation above the horizon; the camera looks north.</summary>
        public static readonly double CameraElevation = MathUtil.Radians(45.0);

        /// <summary>Screen-space vertical stretch applied to the orthographic view.</summary>
        public const double VerticalStretch = 1.25;

        /// <summary>Direction towards the shading light (west, slightly south, above), world space.</summary>
        public static readonly Vec3 ShadeLight = PythonWorldToWorld(new Vec3(-0.70, 0.35, 0.90)).Normalized;

        /// <summary>
        /// Direction towards the light that casts the ground shadow, world space: west, barely south, 41.5
        /// degrees up. Measured on the vanilla engineer's idle sprites: the shadow of the top of his head falls
        /// about 1.28 times his drawn height to the east and 0.05 of it up the screen, which this gives (a point
        /// h tiles up is drawn 0.884 h high and its shadow 1.13 h to the east).
        /// </summary>
        public static readonly Vec3 ShadowLight = PythonWorldToWorld(new Vec3(-1.13, 0.05, 1.0)).Normalized;

        /// <summary>Height of the body above its standing height in the flight animations, tiles.</summary>
        public const double FlyHeight = 0.42;

        /// <summary>
        /// Reference height of a standing character, tiles (the dragon is about this tall): the size Core's
        /// proportions are given at.
        /// </summary>
        public const double DefaultCharacterHeight = 1.5;

        /// <summary>
        /// How tall the vanilla engineer is drawn, tiles: 97 px from boots to the top of the head on screen in the
        /// in-game checks, where a 1.5-tile figure measures 85 px. Avatars are drawn this tall at 100% size.
        /// </summary>
        public const double EngineerHeight = 1.7;

        /// <summary>The Python renderer's world space has y pointing south; Core's points north.</summary>
        public static Vec3 PythonWorldToWorld(Vec3 v) => new Vec3(v.X, -v.Y, v.Z);

        /// <summary>
        /// Rotation placing a model with the given orientation (fraction of a clockwise turn from north)
        /// in the world.
        /// </summary>
        public static Quat ModelToWorld(double orientation) =>
            Quat.FromAxisAngle(Vec3.UnitZ, -MathUtil.Tau * orientation);

        /// <summary>
        /// Transforms a model space point to world space. <paramref name="mirrored"/> mirrors the model
        /// left/right first (x -> -x), as used for the shadow of a sprite the engine draws mirrored.
        /// </summary>
        public static Vec3 ModelPointToWorld(Vec3 p, double orientation, bool mirrored)
        {
            if (mirrored)
            {
                p = new Vec3(-p.X, p.Y, p.Z);
            }
            return ModelToWorld(orientation).Rotate(p);
        }
    }

    /// <summary>
    /// Conversions between Core spaces and Unity's. Unity is left-handed (x right, y up, z forward);
    /// Core is right-handed (x right, y forward, z up). Swapping y and z converts both points and
    /// rotation axes; a rotation keeps its sense in the physical world.
    /// </summary>
    public static class UnitySpace
    {
        /// <summary>Unity (x, y, z) to Core (x, z, y). Lengths are unchanged (no unit conversion).</summary>
        public static Vec3 ToCore(double x, double y, double z) => new Vec3(x, z, y);

        /// <summary>Core to Unity: returns (x, y, z) in Unity's axes.</summary>
        public static (double x, double y, double z) ToUnity(Vec3 v) => (v.X, v.Z, v.Y);

        /// <summary>A Unity quaternion (x, y, z, w) as a Core rotation.</summary>
        public static Quat RotationToCore(double x, double y, double z, double w) => new Quat(w, -x, -z, -y);

        /// <summary>A Core rotation as a Unity quaternion (x, y, z, w).</summary>
        public static (double x, double y, double z, double w) RotationToUnity(Quat q) => (-q.X, -q.Z, -q.Y, q.W);
    }

    /// <summary>An axis-aligned box.</summary>
    public readonly struct Bounds3
    {
        public readonly Vec3 Min;
        public readonly Vec3 Max;

        public Bounds3(Vec3 min, Vec3 max)
        {
            Min = min;
            Max = max;
        }

        public static readonly Bounds3 Empty = new Bounds3(
            new Vec3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity),
            new Vec3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity));

        public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;

        public Bounds3 Encapsulate(Vec3 p) => new Bounds3(Vec3.Min(Min, p), Vec3.Max(Max, p));

        public Bounds3 Union(Bounds3 other) =>
            other.IsEmpty ? this : IsEmpty ? other : new Bounds3(Vec3.Min(Min, other.Min), Vec3.Max(Max, other.Max));

        public Bounds3 Expanded(double margin) =>
            IsEmpty ? this : new Bounds3(Min - new Vec3(margin, margin, margin), Max + new Vec3(margin, margin, margin));

        /// <summary>The eight corners (empty boxes have none).</summary>
        public Vec3[] Corners()
        {
            if (IsEmpty)
            {
                return Array.Empty<Vec3>();
            }
            var c = new Vec3[8];
            for (int i = 0; i < 8; i++)
            {
                c[i] = new Vec3((i & 1) == 0 ? Min.X : Max.X, (i & 2) == 0 ? Min.Y : Max.Y, (i & 4) == 0 ? Min.Z : Max.Z);
            }
            return c;
        }
    }
}

namespace FactorioAvatarExporter.Core
{
    /// <summary>Input checks shared by the camera and the stick figure renderer.</summary>
    internal static class VecChecks
    {
        /// <summary>True when the value is neither NaN nor infinite.</summary>
        public static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        /// <summary>True when no component is NaN or infinite.</summary>
        public static bool IsFinite(Vec3 v) => IsFinite(v.X) && IsFinite(v.Y) && IsFinite(v.Z);
    }
}

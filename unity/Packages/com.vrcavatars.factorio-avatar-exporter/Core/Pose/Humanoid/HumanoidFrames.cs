namespace FactorioAvatarExporter.Core
{
    /// <summary>Rotations between frames given by two directions.</summary>
    internal static class HumanoidFrames
    {
        /// <summary>
        /// The rotation that turns <paramref name="restPrimary"/> exactly into the direction of
        /// <paramref name="posedPrimary"/>, and the part of <paramref name="restSecondary"/> perpendicular to
        /// its primary into the part of <paramref name="posedSecondary"/> perpendicular to its primary. Neither
        /// secondary may be parallel to its primary.
        /// </summary>
        public static Quat Map(Vec3 restPrimary, Vec3 restSecondary, Vec3 posedPrimary, Vec3 posedSecondary) =>
            (Basis(posedPrimary, posedSecondary) * Basis(restPrimary, restSecondary).Inverse).Normalized;

        // the rotation taking +y to the primary direction and +z to the secondary's perpendicular part
        private static Quat Basis(Vec3 primary, Vec3 secondary)
        {
            Vec3 forward = primary.Normalized;
            Vec3 up = (secondary - forward * Vec3.Dot(secondary, forward)).Normalized;
            return Quat.FromMatrix(Vec3.Cross(forward, up), forward, up);
        }
    }
}

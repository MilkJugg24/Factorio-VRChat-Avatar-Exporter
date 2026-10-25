namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// The ground shadow as a projection (docs/vrc-avatar-format.md, "Rendering conventions"): every point
    /// moves along <see cref="Conventions.ShadowLight"/> onto the ground z = 0, so shadows fall east. The
    /// projection is linear, so it works the same in tiles and in meters.
    /// </summary>
    public static class PlanarShadow
    {
        // P' = P - L * (P.z / L.z): x and y move by -L.x / L.z and -L.y / L.z per unit of height
        private static readonly double SlopeX = Conventions.ShadowLight.X / Conventions.ShadowLight.Z;
        private static readonly double SlopeY = Conventions.ShadowLight.Y / Conventions.ShadowLight.Z;

        /// <summary>Where the shadow of world point <paramref name="world"/> falls on the ground (z = 0).</summary>
        public static Vec3 Project(Vec3 world) =>
            new Vec3(world.X - world.Z * SlopeX, world.Y - world.Z * SlopeY, 0);

        /// <summary>
        /// <see cref="Project"/> as a 4x4 matrix in Core world space, for column vectors
        /// (<c>p' = M (x, y, z, 1)</c>). Row-major: element (row, column) is at index <c>row * 4 + column</c>.
        /// A new array each call.
        /// </summary>
        public static double[] WorldMatrix() => new double[]
        {
            1, 0, -SlopeX, 0,
            0, 1, -SlopeY, 0,
            0, 0, 0, 0,
            0, 0, 0, 1,
        };

        /// <summary>
        /// <see cref="Project"/> as a 4x4 matrix in Unity space (x east, y up, z north; any length unit), for
        /// baking shadow-proxy meshes: it flattens points onto Unity's y = 0. Row-major like
        /// <see cref="WorldMatrix"/>, so element (row, column) goes to Unity's <c>Matrix4x4[row, column]</c>.
        /// A new array each call.
        /// </summary>
        public static double[] UnityMatrix()
        {
            // the same map with Core's y and z swapped: M_unity = S M S where S swaps axes 1 and 2
            int[] swap = { 0, 2, 1, 3 };
            double[] core = WorldMatrix();
            var unity = new double[16];
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    unity[row * 4 + column] = core[swap[row] * 4 + swap[column]];
                }
            }
            return unity;
        }
    }
}

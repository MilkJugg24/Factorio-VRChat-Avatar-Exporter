using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>Argument checks shared by the humanoid rig and pose code.</summary>
    internal static class HumanoidArgs
    {
        public static void CheckSide(int side, string paramName = "side")
        {
            if (side != 1 && side != -1)
            {
                throw new ArgumentOutOfRangeException(paramName, side, "side must be +1 (right) or -1 (left)");
            }
        }

        public static void CheckFinite(double value, string paramName)
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentException(paramName + " must be a finite number", paramName);
            }
        }

        public static void CheckFinite(Vec3 value, string paramName)
        {
            if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Z))
            {
                throw new ArgumentException(paramName + " must be finite, got " + value, paramName);
            }
        }

        /// <summary>A rotation argument, normalized; rejects quaternions that are not finite or (almost) zero.</summary>
        public static Quat CheckRotation(Quat value, string paramName)
        {
            double norm = value.Norm;
            if (!double.IsFinite(norm) || norm < 1e-9)
            {
                throw new ArgumentException(paramName + " must be a rotation (a non-zero quaternion), got " + value,
                    paramName);
            }
            return value.Normalized;
        }
    }
}
